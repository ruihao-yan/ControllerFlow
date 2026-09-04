using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;
using ControllerFlow.Core.Routing;

namespace ControllerFlow.Core.Engine;

/// <summary>
/// 路由引擎运行参数。
/// </summary>
public sealed class RoutingEngineOptions
{
    /// <summary>成功匹配并执行后的默认震动反馈（Binding 未覆盖 Feedback 时使用）。</summary>
    public HapticPattern SuccessFeedback { get; init; } = new(0.2, 0.2, TimeSpan.FromMilliseconds(80));

    /// <summary>无 Profile / 无匹配绑定时的默认震动反馈（默认静默）。</summary>
    public HapticPattern NoMatchFeedback { get; init; } = new(0.0, 0.0, TimeSpan.Zero);

    /// <summary>执行失败时的默认震动反馈。</summary>
    public HapticPattern FailureFeedback { get; init; } = new(0.8, 0.8, TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// 长按重复的最小间隔。绑定 Trigger 的 HoldMilliseconds 可作为
    /// 该绑定长按重复间隔的覆盖值，但不会低于该最小值。
    /// </summary>
    public int MinimumHoldRepeatMilliseconds { get; init; } = 25;

    public static RoutingEngineOptions Default { get; } = new();
}

/// <summary>
/// 主链路 Input -> Router -> Profile -> Output 的执行引擎：
/// 负责按下/释放配对（组合键不卡键）、语音会话（按住说话）、
/// 长按重复节流，以及成功 / 无匹配 / 失败三类震动反馈。
/// 引擎只依赖端口接口，不依赖 Windows API。
/// </summary>
public sealed class RoutingEngine
{
    private readonly ProfileRouter _router;
    private readonly IActionExecutor _executor;
    private readonly IHapticFeedback? _haptic;
    private readonly ISpeechToolProcessController? _speechToolController;
    private readonly RoutingEngineOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, List<string>> _heldKeys = new();
    private readonly Dictionary<Guid, SpeechToolSession> _speechSessions = new();
    private readonly Dictionary<Guid, long> _lastRepeatAt = new();

    public RoutingEngine(
        ProfileRouter router,
        IActionExecutor executor,
        IHapticFeedback? haptic = null,
        RoutingEngineOptions? options = null,
        TimeProvider? timeProvider = null,
        ISpeechToolProcessController? speechToolController = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _haptic = haptic;
        _options = options ?? RoutingEngineOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _speechToolController = speechToolController;
    }

    /// <summary>
    /// 暂停路由（例如按键捕获窗口打开时）。暂停期间不路由、不执行、不震动。
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>处理单个输入事件。</summary>
    public async ValueTask<ExecutionOutcome> HandleAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (IsPaused)
        {
            return new ExecutionOutcome(RoutingStatus.Paused, null, null, false);
        }

        var decision = await _router.RouteAsync(input, cancellationToken);

        if (decision.Status is RoutingStatus.NoProfile or RoutingStatus.NoBinding)
        {
            await PlayHapticAsync(input.DeviceId, _options.NoMatchFeedback, cancellationToken);
            return new ExecutionOutcome(decision.Status, decision.Profile, null, false);
        }

        var binding = decision.Binding!;
        try
        {
            var executed = await ExecuteBindingAsync(binding, input, cancellationToken);
            if (executed)
            {
                // 仅在实际执行了输出动作（含配对抬起、语音会话结束）时提供反馈，
                // 避免点按 Binding 的松开事件、节流掉的长按重复触发无意义震动。
                await PlayHapticAsync(
                    input.DeviceId,
                    binding.Feedback ?? _options.SuccessFeedback,
                    cancellationToken);
            }

            return new ExecutionOutcome(RoutingStatus.Matched, decision.Profile, binding, executed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await PlayHapticAsync(input.DeviceId, _options.FailureFeedback, cancellationToken);
            return new ExecutionOutcome(RoutingStatus.Matched, decision.Profile, binding, false, ex.Message);
        }
    }

    /// <summary>
    /// 释放所有被引擎保持的按键与语音会话（应用退出 / 输入源停止时调用），
    /// 避免组合键卡住或语音工具进程残留。
    /// </summary>
    public async ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        List<List<string>> pending;
        List<SpeechToolSession> sessions;
        lock (_sync)
        {
            pending = _heldKeys.Values.ToList();
            _heldKeys.Clear();
            sessions = _speechSessions.Values.ToList();
            _speechSessions.Clear();
        }

        foreach (var keys in pending)
        {
            if (keys.Count == 0)
            {
                continue;
            }

            try
            {
                await _executor.ExecuteAsync(
                    new KeyboardShortcutAction(keys, KeyDownOnly: false, KeyUpOnly: true),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 尽力释放：单个按键抬起失败不应阻塞其余按键。
            }
        }

        foreach (var session in sessions)
        {
            if (_speechToolController is null)
            {
                break;
            }

            try
            {
                await _speechToolController.StopAsync(session, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 尽力结束：语音工具进程已自行退出时忽略。
            }
        }
    }

    private async ValueTask<bool> ExecuteBindingAsync(
        InputBinding binding,
        ControllerInputEvent input,
        CancellationToken cancellationToken)
    {
        switch (binding.Action)
        {
            case KeyboardShortcutAction keyboard when keyboard.KeyDownOnly:
                if (input.Gesture == InputGesture.Pressed)
                {
                    await _executor.ExecuteAsync(keyboard, cancellationToken);
                    TrackHeldKeys(binding.Id, keyboard.Keys);
                    return true;
                }

                if (input.Gesture == InputGesture.Released)
                {
                    await ReleaseHeldKeysAsync(binding.Id, cancellationToken);
                    return true;
                }

                return false;

            case KeyboardShortcutAction keyboard when keyboard.KeyUpOnly:
                if (input.Gesture == InputGesture.Pressed)
                {
                    await _executor.ExecuteAsync(keyboard, cancellationToken);
                    return true;
                }

                return false;

            case KeyboardShortcutAction keyboard:
                if (input.Gesture == InputGesture.Released)
                {
                    return false;
                }

                if (input.Gesture == InputGesture.Held && !ShouldRepeat(binding))
                {
                    return false;
                }

                await _executor.ExecuteAsync(keyboard, cancellationToken);
                return true;

            case SpeechToolAction speech:
                if (input.Gesture == InputGesture.Pressed)
                {
                    if (!string.IsNullOrWhiteSpace(speech.ExecutablePath))
                    {
                        // 进程模式：按住 = 启动语音工具，松开 = 结束会话。
                        if (_speechToolController is null)
                        {
                            throw new InvalidOperationException(
                                "语音动作配置了工具路径，但未注册 ISpeechToolProcessController。");
                        }

                        var session = await _speechToolController.StartAsync(
                            speech.ExecutablePath,
                            speech.Arguments,
                            cancellationToken);
                        if (session is null)
                        {
                            return false;
                        }

                        lock (_sync)
                        {
                            _speechSessions[binding.Id] = session;
                        }

                        return true;
                    }

                    await _executor.ExecuteAsync(speech.Start, cancellationToken);
                    if (speech.Start.KeyDownOnly)
                    {
                        TrackHeldKeys(binding.Id, speech.Start.Keys);
                    }

                    return true;
                }

                if (input.Gesture == InputGesture.Released)
                {
                    if (!string.IsNullOrWhiteSpace(speech.ExecutablePath))
                    {
                        return await EndSpeechSessionAsync(binding.Id, cancellationToken);
                    }

                    await ReleaseHeldKeysAsync(binding.Id, cancellationToken);
                    await _executor.ExecuteAsync(speech.Stop, cancellationToken);
                    return true;
                }

                return false;

            case LaunchApplicationAction launch:
                if (input.Gesture == InputGesture.Pressed)
                {
                    await _executor.ExecuteAsync(launch, cancellationToken);
                    return true;
                }

                return false;

            case MouseAction mouse:
                if (input.Gesture == InputGesture.Released)
                {
                    return false;
                }

                if (input.Gesture == InputGesture.Held && !ShouldRepeat(binding))
                {
                    return false;
                }

                await _executor.ExecuteAsync(mouse, cancellationToken);
                return true;

            case MediaKeyAction media:
                if (input.Gesture == InputGesture.Released)
                {
                    return false;
                }

                if (input.Gesture == InputGesture.Held && !ShouldRepeat(binding))
                {
                    return false;
                }

                await _executor.ExecuteAsync(media, cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private bool ShouldRepeat(InputBinding binding)
    {
        var interval = Math.Max(
            _options.MinimumHoldRepeatMilliseconds,
            binding.Trigger.HoldMilliseconds);

        var now = _timeProvider.GetTimestamp();
        lock (_sync)
        {
            if (_lastRepeatAt.TryGetValue(binding.Id, out var last)
                && _timeProvider.GetElapsedTime(last, now) < TimeSpan.FromMilliseconds(interval))
            {
                return false;
            }

            _lastRepeatAt[binding.Id] = now;
            return true;
        }
    }

    private void TrackHeldKeys(Guid bindingId, IReadOnlyList<string> keys)
    {
        lock (_sync)
        {
            if (!_heldKeys.TryGetValue(bindingId, out var held))
            {
                held = new List<string>();
                _heldKeys[bindingId] = held;
            }

            foreach (var key in keys)
            {
                if (!held.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    held.Add(key);
                }
            }
        }
    }

    private async ValueTask ReleaseHeldKeysAsync(Guid bindingId, CancellationToken cancellationToken)
    {
        List<string>? held;
        lock (_sync)
        {
            if (!_heldKeys.Remove(bindingId, out held) || held.Count == 0)
            {
                return;
            }
        }

        await _executor.ExecuteAsync(
            new KeyboardShortcutAction(held, KeyDownOnly: false, KeyUpOnly: true),
            cancellationToken);
    }

    private async ValueTask<bool> EndSpeechSessionAsync(
        Guid bindingId,
        CancellationToken cancellationToken)
    {
        SpeechToolSession? session;
        lock (_sync)
        {
            if (!_speechSessions.Remove(bindingId, out session))
            {
                return false;
            }
        }

        await _speechToolController!.StopAsync(session, cancellationToken);
        return true;
    }

    private async ValueTask PlayHapticAsync(
        string deviceId,
        HapticPattern pattern,
        CancellationToken cancellationToken)
    {
        if (_haptic is null || pattern.Duration <= TimeSpan.Zero)
        {
            return;
        }

        if (pattern.LeftMotor <= 0 && pattern.RightMotor <= 0)
        {
            return;
        }

        try
        {
            await _haptic.PlayAsync(deviceId, pattern, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 震动反馈失败不应打断主链路。
        }
    }
}
