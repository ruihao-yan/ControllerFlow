using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;
using ControllerFlow.Core.Routing;

namespace ControllerFlow.Core.Engine;

/// <summary>
/// 路由引擎运行参数。
/// </summary>
public sealed class RoutingEngineOptions
{
    /// <summary>无 Profile / 无匹配绑定时的默认震动反馈（默认静默）。</summary>
    public HapticPattern NoMatchFeedback { get; init; } = new(0.0, 0.0, TimeSpan.Zero);

    /// <summary>执行失败时的默认震动反馈。</summary>
    public HapticPattern FailureFeedback { get; init; } = new(0.8, 0.8, TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// 长按重复的最小间隔。Trigger.HoldMilliseconds 为 0 时仅执行一次；
    /// 大于 0 时作为重复间隔，且不会低于该最小值。
    /// </summary>
    public int MinimumHoldRepeatMilliseconds { get; init; } = 25;

    public static RoutingEngineOptions Default { get; } = new();
}

/// <summary>
/// 主链路 Input -> Router -> Profile -> Output 的执行引擎：
/// 负责按下/释放配对（组合键不卡键）、长按重复节流，
/// 以及 Binding / 无匹配 / 失败三类震动反馈。
/// 引擎只依赖端口接口，不依赖 Windows API。
/// </summary>
public sealed class RoutingEngine
{
    private readonly ProfileRouter _router;
    private readonly IActionExecutor _executor;
    private readonly IHapticFeedback? _haptic;
    private readonly RoutingEngineOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _eventGate = new(1, 1);
    private readonly Dictionary<(string Device, string Control), RoutingDecision> _activePresses = new();
    private readonly Dictionary<Guid, List<string>> _heldKeys = new();
    private readonly Dictionary<(string Device, string Control, Guid Binding), long> _lastRepeatAt = new();

    public RoutingEngine(
        ProfileRouter router,
        IActionExecutor executor,
        IHapticFeedback? haptic = null,
        RoutingEngineOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _haptic = haptic;
        _options = options ?? RoutingEngineOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 暂停路由（例如按键捕获窗口打开时）。暂停期间只清理已有按键会话，不执行新绑定。
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>处理单个输入事件。</summary>
    public async ValueTask<ExecutionOutcome> HandleAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await _eventGate.WaitAsync(cancellationToken);
        try
        {
            return await HandleCoreAsync(input, cancellationToken);
        }
        finally
        {
            _eventGate.Release();
        }
    }

    private async ValueTask<ExecutionOutcome> HandleCoreAsync(
        ControllerInputEvent input, CancellationToken cancellationToken)
    {
        var control = (input.DeviceId, input.ControlId.ToUpperInvariant());
        if (input.Gesture is InputGesture.Pressed or InputGesture.Released)
        {
            ClearRepeatState(control);
        }

        RoutingDecision? paired = null;
        if (input.Gesture == InputGesture.Released
            && _activePresses.TryGetValue(control, out paired))
        {
            // 使用按下时保存的绑定结束会话，前台切换和配置编辑不会改变释放目标。
            try
            {
                await ExecuteBindingAsync(paired.Binding!, input, cancellationToken);
                _activePresses.Remove(control);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await PlayHapticAsync(input.DeviceId, _options.FailureFeedback, cancellationToken);
                return new ExecutionOutcome(RoutingStatus.Matched, paired.Profile, paired.Binding, false, ex.Message);
            }
        }

        if (IsPaused)
        {
            return new ExecutionOutcome(RoutingStatus.Paused, paired?.Profile, paired?.Binding, paired is not null);
        }

        var decision = await _router.RouteAsync(input, cancellationToken);

        if (paired is not null && decision.Binding?.Trigger.Gesture != InputGesture.Released)
        {
            return new ExecutionOutcome(RoutingStatus.Matched, paired.Profile, paired.Binding, true);
        }

        if (decision.Status is RoutingStatus.NoProfile or RoutingStatus.NoBinding)
        {
            await PlayHapticAsync(input.DeviceId, _options.NoMatchFeedback, cancellationToken);
            return new ExecutionOutcome(decision.Status, decision.Profile, null, false);
        }

        var binding = decision.Binding!;
        try
        {
            if (binding.Trigger.Gesture != input.Gesture)
            {
                return new ExecutionOutcome(RoutingStatus.Matched, decision.Profile, binding, paired is not null);
            }

            var needsPair = input.Gesture is InputGesture.Pressed or InputGesture.Held
                && binding.Action is KeyboardShortcutAction { KeyDownOnly: true };
            if (needsPair && _activePresses.ContainsKey(control))
            {
                return new ExecutionOutcome(RoutingStatus.Matched, decision.Profile, binding, false);
            }

            var executionBinding = needsPair ? binding with { Id = Guid.NewGuid() } : binding;
            var executed = await ExecuteBindingAsync(executionBinding, input, cancellationToken);
            if (executed && needsPair)
            {
                _activePresses[control] = decision with { Binding = executionBinding };
            }
            if (executed && binding.Feedback is not null)
            {
                // 反馈完全由 Binding 控制；留空时保持静默。
                await PlayHapticAsync(
                    input.DeviceId,
                    binding.Feedback,
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
    /// 释放所有被引擎保持的按键（应用退出 / 输入源停止时调用），
    /// 避免组合键卡住。
    /// </summary>
    public async ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        await _eventGate.WaitAsync(cancellationToken);
        try
        {
            await ReleaseAllCoreAsync(cancellationToken);
        }
        finally
        {
            _eventGate.Release();
        }
    }

    private async ValueTask ReleaseAllCoreAsync(CancellationToken cancellationToken)
    {
        _activePresses.Clear();
        List<List<string>> pending;
        lock (_sync)
        {
            pending = _heldKeys.Values.ToList();
            _heldKeys.Clear();
            _lastRepeatAt.Clear();
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
    }

    private async ValueTask<bool> ExecuteBindingAsync(
        InputBinding binding,
        ControllerInputEvent input,
        CancellationToken cancellationToken)
    {
        switch (binding.Action)
        {
            case KeyboardShortcutAction keyboard when keyboard.KeyDownOnly:
                if (input.Gesture == binding.Trigger.Gesture
                    && input.Gesture is InputGesture.Pressed or InputGesture.Held)
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
                if (input.Gesture == binding.Trigger.Gesture
                    && (input.Gesture != InputGesture.Held || ShouldRepeat(binding, input)))
                {
                    await _executor.ExecuteAsync(keyboard, cancellationToken);
                    return true;
                }

                return false;

            case KeyboardShortcutAction keyboard:
                if (input.Gesture == InputGesture.Released && binding.Trigger.Gesture != InputGesture.Released)
                {
                    return false;
                }

                if (input.Gesture == InputGesture.Held && !ShouldRepeat(binding, input))
                {
                    return false;
                }

                await _executor.ExecuteAsync(keyboard, cancellationToken);
                return true;

            case LaunchApplicationAction launch:
                if (input.Gesture == InputGesture.Held && !ShouldRepeat(binding, input))
                {
                    return false;
                }

                if (input.Gesture == binding.Trigger.Gesture)
                {
                    await _executor.ExecuteAsync(launch, cancellationToken);
                    return true;
                }

                return false;

            case MouseAction mouse:
                if (input.Gesture == InputGesture.Released && binding.Trigger.Gesture != InputGesture.Released)
                {
                    return false;
                }

                if (input.Gesture == InputGesture.Held && !ShouldRepeat(binding, input))
                {
                    return false;
                }

                await _executor.ExecuteAsync(mouse, cancellationToken);
                return true;

            case MediaKeyAction media:
                if (input.Gesture == InputGesture.Released && binding.Trigger.Gesture != InputGesture.Released)
                {
                    return false;
                }

                if (input.Gesture == InputGesture.Held && !ShouldRepeat(binding, input))
                {
                    return false;
                }

                await _executor.ExecuteAsync(media, cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private bool ShouldRepeat(InputBinding binding, ControllerInputEvent input)
    {
        var now = _timeProvider.GetTimestamp();
        var key = (input.DeviceId, input.ControlId.ToUpperInvariant(), binding.Id);
        lock (_sync)
        {
            if (!_lastRepeatAt.TryGetValue(key, out var last))
            {
                _lastRepeatAt[key] = now;
                return true;
            }

            if (binding.Trigger.HoldMilliseconds <= 0)
            {
                return false;
            }

            var interval = Math.Max(
                _options.MinimumHoldRepeatMilliseconds,
                binding.Trigger.HoldMilliseconds);
            if (_timeProvider.GetElapsedTime(last, now) < TimeSpan.FromMilliseconds(interval))
            {
                return false;
            }

            _lastRepeatAt[key] = now;
            return true;
        }
    }

    private void ClearRepeatState((string Device, string Control) control)
    {
        lock (_sync)
        {
            foreach (var key in _lastRepeatAt.Keys
                         .Where(key => key.Device == control.Device && key.Control == control.Control)
                         .ToArray())
            {
                _lastRepeatAt.Remove(key);
            }
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
