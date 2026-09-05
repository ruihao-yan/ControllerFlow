using ControllerFlow.Core.Engine;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;
using ControllerFlow.Core.Routing;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class RoutingEngineTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private readonly RecordingExecutor _executor = new();
    private readonly RecordingHaptic _haptic = new();
    private readonly RecordingSpeechTool _speechTool = new();

    private static readonly ForegroundApp AnyApp = new(1, "any-app", null, "窗口");

    /// <summary>构造引擎：默认 Profile 只含给定的 Binding。</summary>
    private RoutingEngine CreateEngine(
        params InputBinding[] bindings) =>
        CreateEngine(defaultOptions: null, bindings);

    private RoutingEngine CreateEngine(
        RoutingEngineOptions? defaultOptions,
        params InputBinding[] bindings)
    {
        var profiles = new[] { TestProfiles.DefaultProfile(bindings: bindings) };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(AnyApp),
            new StubProfileRepository(profiles));

        return new RoutingEngine(
            router,
            _executor,
            _haptic,
            defaultOptions ?? RoutingEngineOptions.Default,
            _time,
            _speechTool);
    }

    private static ControllerInputEvent Event(string controlId, InputGesture gesture) =>
        new("pad-1", controlId, gesture, DateTimeOffset.UtcNow);

    [Fact]
    public async Task HandleAsync_KeyboardTap_ExecutesOnPress()
    {
        var engine = CreateEngine(TestProfiles.Binding("A", action: new KeyboardShortcutAction(["Ctrl", "C"])));

        var outcome = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.Equal(RoutingStatus.Matched, outcome.Status);
        Assert.True(outcome.ActionExecuted);
        Assert.Null(outcome.Error);
        var action = Assert.Single(_executor.Executed);
        Assert.Equal(["Ctrl", "C"], Assert.IsType<KeyboardShortcutAction>(action).Keys);
        Assert.True(_haptic.Played.Count == 1, "按下成功应触发默认成功震动。");
    }

    [Fact]
    public async Task HandleAsync_KeyDownOnly_HoldsAndPairsRelease()
    {
        var engine = CreateEngine(TestProfiles.Binding(
            "A",
            action: new KeyboardShortcutAction(["Ctrl", "Alt"], KeyDownOnly: true)));

        var press = await engine.HandleAsync(Event("A", InputGesture.Pressed));
        Assert.True(press.ActionExecuted);
        Assert.Single(_executor.Executed);

        // 释放事件：先配对被保持的按键抬起的动作。
        var release = await engine.HandleAsync(Event("A", InputGesture.Released));
        Assert.True(release.ActionExecuted);

        Assert.Equal(2, _executor.Executed.Count);
        var keyUp = Assert.IsType<KeyboardShortcutAction>(_executor.Executed[1]);
        Assert.True(keyUp.KeyUpOnly);
        Assert.False(keyUp.KeyDownOnly);
        Assert.Equal(["Ctrl", "Alt"], keyUp.Keys);
    }

    [Fact]
    public async Task HandleAsync_Held_ThrottlesRepeatByBindingInterval()
    {
        var engine = CreateEngine(TestProfiles.Binding(
            "A",
            gesture: InputGesture.Held,
            holdMilliseconds: 100,
            action: new KeyboardShortcutAction(["Space"])));

        Assert.Equal(RoutingStatus.NoBinding, (await engine.HandleAsync(Event("A", InputGesture.Pressed))).Status);

        // 第一个 Held：立即执行。
        var first = await engine.HandleAsync(Event("A", InputGesture.Held));
        Assert.True(first.ActionExecuted);

        // 50ms 后：间隔内节流，不执行（不震动）。
        _time.Advance(TimeSpan.FromMilliseconds(50));
        var throttled = await engine.HandleAsync(Event("A", InputGesture.Held));
        Assert.False(throttled.ActionExecuted);

        // 再过 50ms：满 100ms 间隔，再次执行。
        _time.Advance(TimeSpan.FromMilliseconds(50));
        var second = await engine.HandleAsync(Event("A", InputGesture.Held));
        Assert.True(second.ActionExecuted);

        Assert.Equal(2, _executor.Executed.Count);
    }

    [Fact]
    public async Task HandleAsync_MouseAndMedia_RepeatOnHeld()
    {
        var engine = CreateEngine(
            TestProfiles.Binding("A", gesture: InputGesture.Held, action: new MouseAction(MouseOperation.ScrollVertical, 120)),
            TestProfiles.Binding("B", gesture: InputGesture.Held, action: new MediaKeyAction(KeyCode.VolumeUp)));

        var first = await engine.HandleAsync(Event("A", InputGesture.Held));
        var second = await engine.HandleAsync(Event("B", InputGesture.Held));

        Assert.True(first.ActionExecuted);
        Assert.True(second.ActionExecuted);
        Assert.Contains(_executor.Executed, a => a is MouseAction { Operation: MouseOperation.ScrollVertical, Amount: 120 });
        Assert.Contains(_executor.Executed, a => a is MediaKeyAction { Key: KeyCode.VolumeUp });
    }

    [Fact]
    public async Task HandleAsync_LaunchApplication_OnPressOnly()
    {
        var engine = CreateEngine(TestProfiles.Binding(
            "A",
            gesture: InputGesture.Pressed,
            action: new LaunchApplicationAction(@"C:\tools\app.exe", "--flag")));

        var press = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.True(press.ActionExecuted);
        var launch = Assert.IsType<LaunchApplicationAction>(Assert.Single(_executor.Executed));
        Assert.Equal(@"C:\tools\app.exe", launch.ExecutablePath);
    }

    [Fact]
    public async Task HandleAsync_NoProfile_NoMatchHaptic_NoExecute()
    {
        var options = new RoutingEngineOptions
        {
            NoMatchFeedback = new HapticPattern(0.1, 0.1, TimeSpan.FromMilliseconds(50))
        };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(AnyApp),
            new StubProfileRepository([]));
        var engine = new RoutingEngine(router, _executor, _haptic, options, _time, _speechTool);

        var outcome = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.Equal(RoutingStatus.NoProfile, outcome.Status);
        Assert.False(outcome.ActionExecuted);
        Assert.Empty(_executor.Executed);
        var (deviceId, pattern) = Assert.Single(_haptic.Played);
        Assert.Equal("pad-1", deviceId);
        Assert.Equal(options.NoMatchFeedback, pattern);
    }

    [Fact]
    public async Task HandleAsync_NoBinding_NoMatchHaptic()
    {
        var options = new RoutingEngineOptions
        {
            NoMatchFeedback = new HapticPattern(0.1, 0.1, TimeSpan.FromMilliseconds(50))
        };
        var engine = CreateEngine(options, TestProfiles.Binding("B"));

        var outcome = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.Equal(RoutingStatus.NoBinding, outcome.Status);
        Assert.NotNull(outcome.Profile);
        Assert.Equal(options.NoMatchFeedback, Assert.Single(_haptic.Played).Pattern);
    }

    [Fact]
    public async Task HandleAsync_ExecutionFailure_FailureHapticAndError()
    {
        _executor.ThrowOnExecute = new InvalidOperationException("模拟输出失败");
        var engine = CreateEngine(TestProfiles.Binding("A"));

        var outcome = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.Equal(RoutingStatus.Matched, outcome.Status);
        Assert.False(outcome.ActionExecuted);
        Assert.Contains("模拟输出失败", outcome.Error, StringComparison.Ordinal);
        Assert.Equal(RoutingEngineOptions.Default.FailureFeedback, Assert.Single(_haptic.Played).Pattern);
    }

    [Fact]
    public async Task HandleAsync_ExecutorCancellation_Rethrows()
    {
        _executor.ThrowOnExecute = new OperationCanceledException();
        var engine = CreateEngine(TestProfiles.Binding("A"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.HandleAsync(Event("A", InputGesture.Pressed)).AsTask());
    }

    [Fact]
    public async Task HandleAsync_Paused_ReturnsPausedWithoutExecuting()
    {
        var engine = CreateEngine(TestProfiles.Binding("A"));
        engine.IsPaused = true;

        var outcome = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.Equal(RoutingStatus.Paused, outcome.Status);
        Assert.Empty(_executor.Executed);
        Assert.Empty(_haptic.Played);
    }

    [Fact]
    public async Task HandleAsync_BindingFeedback_OverridesDefault()
    {
        var feedback = new HapticPattern(0.9, 0.1, TimeSpan.FromMilliseconds(50));
        var engine = CreateEngine(TestProfiles.Binding("A", feedback: feedback));

        _ = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.Equal(feedback, Assert.Single(_haptic.Played).Pattern);
    }

    [Fact]
    public async Task HandleAsync_ZeroDurationFeedback_NoHaptic()
    {
        // 时长为零的反馈（无论是 Binding 覆盖还是选项）不触发震动调用。
        var engine = CreateEngine(TestProfiles.Binding(
            "A",
            feedback: new HapticPattern(0.5, 0.5, TimeSpan.Zero)));

        _ = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.Empty(_haptic.Played);
    }

    [Fact]
    public async Task Speech_HotkeyMode_PressStartsReleaseStops()
    {
        var engine = CreateEngine(TestProfiles.Binding(
            "A",
            action: new SpeechToolAction(
                new KeyboardShortcutAction(["Ctrl", "Space"], KeyDownOnly: true),
                new KeyboardShortcutAction(["Space"]))));

        var press = await engine.HandleAsync(Event("A", InputGesture.Pressed));
        Assert.True(press.ActionExecuted);
        Assert.Single(_executor.Executed);

        var release = await engine.HandleAsync(Event("A", InputGesture.Released));
        Assert.True(release.ActionExecuted);

        // 释放序列：Ctrl+Space 抬起（配对），Space 点按（停止）。
        Assert.Equal(3, _executor.Executed.Count);
        var keyUp = Assert.IsType<KeyboardShortcutAction>(_executor.Executed[1]);
        Assert.True(keyUp.KeyUpOnly);
        Assert.Equal(["Ctrl", "Space"], keyUp.Keys);
        Assert.Equal(["Space"], Assert.IsType<KeyboardShortcutAction>(_executor.Executed[2]).Keys);
    }

    [Fact]
    public async Task Speech_ProcessMode_PressStartsSessionReleaseStopsIt()
    {
        var engine = CreateEngine(TestProfiles.Binding(
            "A",
            action: new SpeechToolAction(
                new KeyboardShortcutAction([]),
                new KeyboardShortcutAction([]),
                ExecutablePath: @"C:\tools\stt.exe",
                Arguments: "--ptt")));

        var press = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.True(press.ActionExecuted);
        var session = Assert.Single(_speechTool.Started);
        Assert.Equal(@"C:\tools\stt.exe", session.ExecutablePath);

        // 长按不重复启动会话。
        Assert.Equal(RoutingStatus.NoBinding, (await engine.HandleAsync(Event("A", InputGesture.Held))).Status);

        var release = await engine.HandleAsync(Event("A", InputGesture.Released));
        Assert.True(release.ActionExecuted);
        Assert.Equal(session.Id, Assert.Single(_speechTool.Stopped).Id);
        Assert.Empty(_executor.Executed);
    }

    [Fact]
    public async Task Speech_ProcessMode_StartFailure_FailureFeedback()
    {
        _speechTool.ThrowOnStart = new FileNotFoundException("工具不存在", @"C:\tools\stt.exe");
        var engine = CreateEngine(TestProfiles.Binding(
            "A",
            action: new SpeechToolAction(
                new KeyboardShortcutAction([]),
                new KeyboardShortcutAction([]),
                ExecutablePath: @"C:\tools\stt.exe")));

        var outcome = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.False(outcome.ActionExecuted);
        Assert.Contains("工具不存在", outcome.Error, StringComparison.Ordinal);
        Assert.Equal(RoutingEngineOptions.Default.FailureFeedback, Assert.Single(_haptic.Played).Pattern);
    }

    [Fact]
    public async Task Speech_ProcessMode_WithoutController_ReturnsError()
    {
        var profiles = new[] { TestProfiles.DefaultProfile(TestProfiles.Binding(
            "A",
            action: new SpeechToolAction(
                new KeyboardShortcutAction([]),
                new KeyboardShortcutAction([]),
                ExecutablePath: @"C:\tools\stt.exe"))) };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(AnyApp),
            new StubProfileRepository(profiles));
        var engine = new RoutingEngine(router, _executor, _haptic, null, _time, speechToolController: null);

        var outcome = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        Assert.False(outcome.ActionExecuted);
        Assert.Contains("ISpeechToolProcessController", outcome.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleaseAllAsync_ReleasesHeldKeys()
    {
        var engine = CreateEngine(TestProfiles.Binding(
            "A",
            action: new KeyboardShortcutAction(["Ctrl", "Shift"], KeyDownOnly: true)));
        _ = await engine.HandleAsync(Event("A", InputGesture.Pressed));

        await engine.ReleaseAllAsync();

        var keyUp = Assert.IsType<KeyboardShortcutAction>(
            Assert.Single(_executor.Executed, a => a is KeyboardShortcutAction { KeyUpOnly: true }));
        Assert.True(keyUp.KeyUpOnly);
        Assert.Equal(["Ctrl", "Shift"], keyUp.Keys);

        // 释放后再次调用无动作。
        await engine.ReleaseAllAsync();
        Assert.Equal(2, _executor.Executed.Count);
    }

    [Fact]
    public async Task ReleaseAllAsync_StopsActiveSpeechSessions()
    {
        var engine = CreateEngine(TestProfiles.Binding(
            "A",
            action: new SpeechToolAction(
                new KeyboardShortcutAction([]),
                new KeyboardShortcutAction([]),
                ExecutablePath: @"C:\tools\stt.exe")));
        _ = await engine.HandleAsync(Event("A", InputGesture.Pressed));
        Assert.Single(_speechTool.Started);

        await engine.ReleaseAllAsync();

        Assert.Equal(_speechTool.Started[0].Id, Assert.Single(_speechTool.Stopped).Id);
    }

    [Fact]
    public async Task HandleAsync_HeldRepeat_RespectsMinimumIntervalFloor()
    {
        var options = new RoutingEngineOptions { MinimumHoldRepeatMilliseconds = 25 };
        var engine = CreateEngine(options, TestProfiles.Binding(
            "A",
            gesture: InputGesture.Held,
            action: new KeyboardShortcutAction(["Space"])));

        _ = await engine.HandleAsync(Event("A", InputGesture.Held));       // 执行
        _time.Advance(TimeSpan.FromMilliseconds(20));
        _ = await engine.HandleAsync(Event("A", InputGesture.Held));       // 节流
        _time.Advance(TimeSpan.FromMilliseconds(10));
        _ = await engine.HandleAsync(Event("A", InputGesture.Held));       // 执行（30ms ≥ 25ms）

        Assert.Equal(2, _executor.Executed.Count);
    }

    [Fact]
    public async Task HandleAsync_NullInput_Throws()
    {
        var engine = CreateEngine();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => engine.HandleAsync(null!).AsTask());
    }
    [Fact]
    public async Task Release_AfterForegroundChange_ReleasesOriginalKeys()
    {
        ForegroundApp? current = AnyApp;
        var binding = TestProfiles.Binding("A", action: new KeyboardShortcutAction(["Ctrl"], KeyDownOnly: true));
        var profile = TestProfiles.AppProfile("App", new AppMatchRule(ProcessName: AnyApp.ProcessName), bindings: [binding]);
        var router = new ProfileRouter(new ScriptedForegroundAppProvider(() => current), new StubProfileRepository([profile]));
        var engine = new RoutingEngine(router, _executor);
        await engine.HandleAsync(Event("A", InputGesture.Pressed));
        current = null;

        var result = await engine.HandleAsync(Event("A", InputGesture.Released));

        Assert.True(result.ActionExecuted);
        Assert.True(Assert.IsType<KeyboardShortcutAction>(_executor.Executed[1]).KeyUpOnly);
        await engine.ReleaseAllAsync();
        Assert.Equal(2, _executor.Executed.Count);
    }

    [Fact]
    public async Task Release_WithExplicitBinding_CleansUpBeforeExecutingReleaseAction()
    {
        var engine = CreateEngine(
            TestProfiles.Binding("A", action: new KeyboardShortcutAction(["Ctrl"], KeyDownOnly: true)),
            TestProfiles.Binding("A", InputGesture.Released, new KeyboardShortcutAction(["C"])));
        await engine.HandleAsync(Event("A", InputGesture.Pressed));

        await engine.HandleAsync(Event("A", InputGesture.Released));

        Assert.Equal(3, _executor.Executed.Count);
        Assert.True(Assert.IsType<KeyboardShortcutAction>(_executor.Executed[1]).KeyUpOnly);
        Assert.Equal(["C"], Assert.IsType<KeyboardShortcutAction>(_executor.Executed[2]).Keys);
    }

    [Fact]
    public async Task Release_WhilePaused_CleansUpActivePress()
    {
        var engine = CreateEngine(TestProfiles.Binding("A", action: new KeyboardShortcutAction(["Ctrl"], KeyDownOnly: true)));
        await engine.HandleAsync(Event("A", InputGesture.Pressed));
        engine.IsPaused = true;

        var result = await engine.HandleAsync(Event("A", InputGesture.Released));

        Assert.True(result.ActionExecuted);
        Assert.True(Assert.IsType<KeyboardShortcutAction>(_executor.Executed[1]).KeyUpOnly);
    }

    [Fact]
    public async Task ExplicitRelease_ExecutesKeyboardMouseMediaAndLaunch()
    {
        OutputAction[] actions = [
            new KeyboardShortcutAction(["C"]),
            new MouseAction(MouseOperation.ScrollVertical, -120),
            new MediaKeyAction(KeyCode.VolumeUp),
            new LaunchApplicationAction("app.exe")];
        foreach (var action in actions)
        {
            var engine = CreateEngine(TestProfiles.Binding("A", InputGesture.Released, action));
            Assert.False((await engine.HandleAsync(Event("A", InputGesture.Pressed))).ActionExecuted);
            Assert.True((await engine.HandleAsync(Event("A", InputGesture.Released))).ActionExecuted);
        }
        Assert.Equal(actions, _executor.Executed);
    }

    [Fact]
    public async Task TapRelease_DoesNotRepeatPressAction()
    {
        var engine = CreateEngine(TestProfiles.Binding("A"));
        await engine.HandleAsync(Event("A", InputGesture.Pressed));
        Assert.False((await engine.HandleAsync(Event("A", InputGesture.Released))).ActionExecuted);
        Assert.Single(_executor.Executed);
    }

    [Fact]
    public async Task Speech_AfterForegroundChange_StopsOriginalSession()
    {
        ForegroundApp? current = AnyApp;
        var binding = TestProfiles.Binding("A", action: new SpeechToolAction(
            new KeyboardShortcutAction([]), new KeyboardShortcutAction([]), "stt.exe"));
        var profile = TestProfiles.AppProfile("App", new AppMatchRule(ProcessName: AnyApp.ProcessName), bindings: [binding]);
        var engine = new RoutingEngine(new ProfileRouter(
            new ScriptedForegroundAppProvider(() => current), new StubProfileRepository([profile])),
            _executor, speechToolController: _speechTool);
        await engine.HandleAsync(Event("A", InputGesture.Pressed));
        current = null;
        await engine.HandleAsync(Event("A", InputGesture.Released));
        Assert.Equal(Assert.Single(_speechTool.Started), Assert.Single(_speechTool.Stopped));
    }

    [Fact]
    public async Task Speech_TwoDevices_ReleaseOnlyTheirOwnSessions()
    {
        var engine = CreateEngine(TestProfiles.Binding("A", action: new SpeechToolAction(
            new KeyboardShortcutAction([]), new KeyboardShortcutAction([]), "stt.exe")));
        await engine.HandleAsync(Event("A", InputGesture.Pressed));
        await engine.HandleAsync(Event("A", InputGesture.Pressed) with { DeviceId = "pad-2" });
        await engine.HandleAsync(Event("A", InputGesture.Released));
        Assert.Equal(_speechTool.Started[0], Assert.Single(_speechTool.Stopped));
        await engine.HandleAsync(Event("A", InputGesture.Released) with { DeviceId = "pad-2" });
        Assert.Equal(_speechTool.Started, _speechTool.Stopped);
    }

}