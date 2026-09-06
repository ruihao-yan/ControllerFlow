using ControllerFlow.Core.Engine;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;
using ControllerFlow.Core.Profiles;
using ControllerFlow.Core.Routing;
using ControllerFlow.Windows.Desktop;
using ControllerFlow.Windows.Input;
using Windows.Gaming.Input;
using Xunit;

namespace ControllerFlow.Windows.Tests;

/// <summary>
/// 真实手柄诊断：运行测试时请将目标应用置于前台，并长按一次 RB 后松开。
/// 手柄未连接、Profile 未命中、Binding 不存在或快捷键未执行时均返回失败。
/// </summary>
public sealed class VibeCodingHardwareTests
{
    [Fact]
    [Trait("Category", "InteractiveHardware")]
    public async Task VibeCoding_RbLongHold_ExecutesConfiguredKeyboardShortcut()
    {
        var result = await RunDiagnosticAsync();
        Console.WriteLine(result.Detail);
        Assert.True(result.Passed, result.Detail);
    }

    private static async Task<DiagnosticResult> RunDiagnosticAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Fail("当前平台不是 Windows，手柄结果为 false。");
        }

        int gamepadCount;
        try
        {
            gamepadCount = GamepadCompatibility.GetConnectedCount();
        }
        catch (Exception ex)
        {
            return Fail($"读取手柄失败，结果为 false：{ex.Message}");
        }

        if (gamepadCount == 0)
        {
            return Fail("未检测到已连接手柄，结果为 false。");
        }

        var wgiCount = Gamepad.Gamepads.Count;
        var xinputCount = XInputNative.GetConnectedCount();
        Console.WriteLine($"手柄后端：WGI={wgiCount}，XInput={xinputCount}");

        var profilesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ControllerFlow",
            "profiles.json");
        if (!File.Exists(profilesPath))
        {
            return Fail($"未找到 Profile 文件：{profilesPath}，结果为 false。");
        }

        IReadOnlyList<ControllerProfile> profiles;
        try
        {
            profiles = await new JsonProfileStore(profilesPath).LoadAsync();
        }
        catch (Exception ex)
        {
            return Fail($"读取 Profile 失败，结果为 false：{ex.Message}");
        }

        var vibeProfile = profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name.Trim(), "vibe coding", StringComparison.OrdinalIgnoreCase));
        if (vibeProfile is null)
        {
            return Fail("未找到名为 vibe coding 的 Profile，结果为 false。");
        }

        var foregroundProvider = new Win32ForegroundAppProvider();
        var foregroundApp = await WaitForMatchingForegroundAsync(
            foregroundProvider,
            vibeProfile,
            TimeSpan.FromSeconds(10));
        if (foregroundApp is null)
        {
            return Fail("10 秒内前台未命中 vibe coding，请切换到 ChatGPT，结果为 false。");
        }

        var binding = vibeProfile.Bindings.FirstOrDefault(candidate =>
            candidate.Enabled
            && string.Equals(candidate.Trigger.ControlId, GamepadControls.RightBumper, StringComparison.OrdinalIgnoreCase)
            && candidate.Trigger.Gesture == InputGesture.Held);
        if (binding is null)
        {
            return Fail("vibe coding 没有启用的 RB 长按 Binding，结果为 false。");
        }

        if (binding.Action is not KeyboardShortcutAction expectedAction
            || !expectedAction.KeyDownOnly
            || expectedAction.KeyUpOnly)
        {
            return Fail("RB 长按 Binding 未启用保持组合键模式，结果为 false。");
        }

        var router = new ProfileRouter(
            new FixedForegroundAppProvider(foregroundApp),
            new FixedProfileRepository([vibeProfile]));
        var engine = new RoutingEngine(
            router,
            new ControllerFlow.Windows.Output.Win32ActionExecutor(),
            options: new RoutingEngineOptions
            {
                NoMatchFeedback = new HapticPattern(0, 0, TimeSpan.Zero),
                FailureFeedback = new HapticPattern(0, 0, TimeSpan.Zero)
            });
        var source = new WindowsGamepadSource();
        var heldResult = new TaskCompletionSource<ExecutionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResult = new TaskCompletionSource<ExecutionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var processingError = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observedEvents = new List<string>();
        var observedRawButtons = new List<string>();
        var observedEventsSync = new object();

        async Task HandleInputAsync(ControllerInputEvent input)
        {
            lock (observedEventsSync)
            {
                observedEvents.Add($"{input.DeviceId}/{input.ControlId}/{input.Gesture}");
            }

            if (!string.Equals(input.ControlId, GamepadControls.RightBumper, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var outcome = await engine.HandleAsync(input);
                if (input.Gesture == InputGesture.Held)
                {
                    heldResult.TrySetResult(outcome);
                }
                else if (input.Gesture == InputGesture.Released)
                {
                    releaseResult.TrySetResult(outcome);
                }
            }
            catch (Exception ex)
            {
                processingError.TrySetResult(ex);
            }
        }

        source.InputReceived += (_, input) => _ = HandleInputAsync(input);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rawProbeTask = Task.Run(async () =>
        {
            ushort? previous = null;
            while (!timeout.IsCancellationRequested)
            {
                if (XInputNative.TryGetState(0, out var state)
                    && state.Gamepad.Buttons != previous)
                {
                    lock (observedEventsSync)
                    {
                        observedRawButtons.Add($"0x{state.Gamepad.Buttons:X4}");
                    }

                    previous = state.Gamepad.Buttons;
                }

                await Task.Delay(10);
            }
        });
        try
        {
            await source.StartAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            return Fail($"启动手柄输入源失败，结果为 false：{ex.Message}");
        }

        try
        {
            var firstEvent = await Task.WhenAny(
                heldResult.Task,
                processingError.Task,
                Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token));
            if (firstEvent == processingError.Task)
            {
                return Fail($"处理 RB 事件失败，结果为 false：{processingError.Task.Result.Message}");
            }

            if (firstEvent != heldResult.Task)
            {
                string observed;
                string rawButtons;
                lock (observedEventsSync)
                {
                    observed = observedEvents.Count == 0
                        ? "无"
                        : string.Join("，", observedEvents.TakeLast(20));
                    rawButtons = observedRawButtons.Count == 0
                        ? "无"
                        : string.Join("→", observedRawButtons.TakeLast(20));
                }

                return Fail(
                    $"20 秒内未收到 RB 长按事件，后端 WGI={wgiCount}/XInput={xinputCount}，原始按钮：{rawButtons}，已观察事件：{observed}，结果为 false。");
            }

            var outcome = await heldResult.Task;
            if (!outcome.ActionExecuted)
            {
                return Fail(
                    $"RB 长按已产生事件但快捷键未执行（状态：{outcome.Status}，错误：{outcome.Error ?? "无"}），结果为 false。");
            }

            if (outcome.Binding?.Action is not KeyboardShortcutAction actualAction
                || !actualAction.Keys.SequenceEqual(expectedAction.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var actualKeys = outcome.Binding?.Action is KeyboardShortcutAction mismatchedAction
                    ? string.Join("+", mismatchedAction.Keys)
                    : "无键盘输出";
                return Fail(
                    $"RB 长按输出不匹配，期望 {string.Join("+", expectedAction.Keys)}，实际 {actualKeys}，结果为 false。");
            }

            var releaseEvent = await Task.WhenAny(
                releaseResult.Task,
                Task.Delay(TimeSpan.FromSeconds(3)));
            if (releaseEvent != releaseResult.Task)
            {
                return Fail("RB 长按后未收到释放事件，结果为 false。");
            }

            var releaseOutcome = await releaseResult.Task;
            if (!releaseOutcome.ActionExecuted)
            {
                return Fail(
                    $"RB 释放后组合键未抬起（错误：{releaseOutcome.Error ?? "无"}），结果为 false。");
            }

            if (GamepadCompatibility.GetConnectedCount() == 0)
            {
                return Fail("测试过程中手柄已断开，结果为 false。");
            }

            return new DiagnosticResult(
                true,
                $"通过：Profile=vibe coding，RB 长按保持 {string.Join("+", expectedAction.Keys)}，释放 RB 后抬起组合键。");
        }
        finally
        {
            timeout.Cancel();
            await source.StopAsync(CancellationToken.None);
            await rawProbeTask;
            await engine.ReleaseAllAsync(CancellationToken.None);
        }
    }

    private static async Task<ForegroundApp?> WaitForMatchingForegroundAsync(
        IForegroundAppProvider provider,
        ControllerProfile profile,
        TimeSpan timeout)
    {
        var resolver = new ProfileResolver(new FixedProfileRepository([profile]));
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var app = await provider.GetCurrentAsync(CancellationToken.None);
            if (app is not null
                && (await resolver.ResolveAsync(app)).Profile?.Id == profile.Id)
            {
                return app;
            }

            await Task.Delay(100);
        }

        return null;
    }

    private static DiagnosticResult Fail(string detail) => new(false, detail);

    private sealed record DiagnosticResult(bool Passed, string Detail);

    private sealed class FixedForegroundAppProvider(ForegroundApp app) : IForegroundAppProvider
    {
        public ValueTask<ForegroundApp?> GetCurrentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ForegroundApp?>(app);
    }

    private sealed class FixedProfileRepository(IReadOnlyList<ControllerProfile> profiles) : IProfileRepository
    {
        public ValueTask<IReadOnlyList<ControllerProfile>> GetEnabledAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(profiles);
    }
}
