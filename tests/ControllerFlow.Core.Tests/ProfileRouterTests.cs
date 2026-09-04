using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;
using ControllerFlow.Core.Routing;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class ProfileRouterTests
{
    [Fact]
    public async Task RouteAsync_PrefersMatchingAppProfileOverDefaultProfile()
    {
        var appBinding = CreateBinding("A");
        var defaultBinding = CreateBinding("A");
        var profiles = new[]
        {
            CreateProfile("Default", true, 0, [], [defaultBinding]),
            CreateProfile(
                "Visual Studio Code",
                false,
                10,
                [new AppMatchRule(ProcessName: "Code")],
                [appBinding])
        };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(new ForegroundApp(1, "Code", null, "Program.cs")),
            new StubProfileRepository(profiles));

        var result = await router.RouteAsync(
            new ControllerInputEvent("pad-1", "A", InputGesture.Pressed, DateTimeOffset.UtcNow));

        Assert.Equal(RoutingStatus.Matched, result.Status);
        Assert.Equal("Visual Studio Code", result.Profile?.Name);
        Assert.Equal(appBinding.Id, result.Binding?.Id);
    }

    [Fact]
    public async Task RouteAsync_UsesDefaultProfileWhenAppDoesNotMatch()
    {
        var defaultBinding = CreateBinding("B");
        var profiles = new[]
        {
            CreateProfile("Default", true, 0, [], [defaultBinding]),
            CreateProfile(
                "Browser",
                false,
                10,
                [new AppMatchRule(ProcessName: "chrome")],
                [])
        };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(new ForegroundApp(2, "notepad", null, "notes.txt")),
            new StubProfileRepository(profiles));

        var result = await router.RouteAsync(
            new ControllerInputEvent("pad-1", "B", InputGesture.Pressed, DateTimeOffset.UtcNow));

        Assert.Equal(RoutingStatus.Matched, result.Status);
        Assert.Equal("Default", result.Profile?.Name);
    }

    [Fact]
    public async Task RouteAsync_NoProfiles_ReturnsNoProfile()
    {
        var router = new ProfileRouter(
            new StubForegroundAppProvider(new ForegroundApp(1, "Code", null, "x.cs")),
            new StubProfileRepository([]));

        var result = await router.RouteAsync(
            new ControllerInputEvent("pad-1", "A", InputGesture.Pressed, DateTimeOffset.UtcNow));

        Assert.Equal(RoutingStatus.NoProfile, result.Status);
        Assert.Null(result.Profile);
        Assert.Null(result.Binding);
    }

    [Fact]
    public async Task RouteAsync_NoBinding_WhenControlUnmapped()
    {
        var profiles = new[]
        {
            CreateProfile("Default", true, 0, [], [CreateBinding("B")])
        };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(new ForegroundApp(1, "Code", null, "x.cs")),
            new StubProfileRepository(profiles));

        var result = await router.RouteAsync(
            new ControllerInputEvent("pad-1", "A", InputGesture.Pressed, DateTimeOffset.UtcNow));

        Assert.Equal(RoutingStatus.NoBinding, result.Status);
        Assert.Equal("Default", result.Profile?.Name);
        Assert.Null(result.Binding);
    }

    [Fact]
    public async Task RouteAsync_GestureMismatch_ReturnsNoBinding()
    {
        var profiles = new[]
        {
            CreateProfile("Default", true, 0, [], [CreateBinding("A")])
        };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(new ForegroundApp(1, "Code", null, "x.cs")),
            new StubProfileRepository(profiles));

        var result = await router.RouteAsync(
            new ControllerInputEvent("pad-1", "A", InputGesture.Held, DateTimeOffset.UtcNow));

        Assert.Equal(RoutingStatus.NoBinding, result.Status);
    }

    [Fact]
    public async Task RouteAsync_DisabledBinding_IsNotMatched()
    {
        var profiles = new[]
        {
            CreateProfile(
                "Default",
                true,
                0,
                [],
                [CreateBinding("A") with { Enabled = false }])
        };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(new ForegroundApp(1, "Code", null, "x.cs")),
            new StubProfileRepository(profiles));

        var result = await router.RouteAsync(
            new ControllerInputEvent("pad-1", "A", InputGesture.Pressed, DateTimeOffset.UtcNow));

        Assert.Equal(RoutingStatus.NoBinding, result.Status);
    }

    [Fact]
    public async Task RouteAsync_TitlePatternRule_SelectsProfile()
    {
        var binding = CreateBinding("A");
        var profiles = new[]
        {
            CreateProfile(
                "编辑器-代码页",
                false,
                10,
                [new AppMatchRule(WindowTitlePattern: @"\.cs")],
                [binding])
        };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(new ForegroundApp(1, "Code", null, "Program.cs")),
            new StubProfileRepository(profiles));

        var result = await router.RouteAsync(
            new ControllerInputEvent("pad-1", "A", InputGesture.Pressed, DateTimeOffset.UtcNow));

        Assert.Equal(RoutingStatus.Matched, result.Status);
        Assert.Equal(binding.Id, result.Binding?.Id);
    }

    [Fact]
    public async Task RouteAsync_ReleasedEvent_FallsBackToPressedGestureBinding()
    {
        // 释放配对：同控件的 Pressed 手势 Binding 接收 Released 事件，供引擎配对抬起。
        var binding = CreateBinding("A");
        var profiles = new[]
        {
            CreateProfile("Default", true, 0, [], [binding])
        };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(new ForegroundApp(1, "Code", null, "x.cs")),
            new StubProfileRepository(profiles));

        var result = await router.RouteAsync(
            new ControllerInputEvent("pad-1", "A", InputGesture.Released, DateTimeOffset.UtcNow));

        Assert.Equal(RoutingStatus.Matched, result.Status);
        Assert.Equal(binding.Id, result.Binding?.Id);
    }

    [Fact]
    public async Task RouteAsync_ReleasedEvent_PrefersExactReleasedBinding()
    {
        // 用户为同一控件配置了明确的 Released 手势 Binding 时，优先精确匹配。
        var pressedBinding = CreateBinding("A");
        var releasedBinding = CreateBinding("A") with
        {
            Trigger = new ControllerTrigger("A", InputGesture.Released)
        };
        var profiles = new[]
        {
            CreateProfile("Default", true, 0, [], [pressedBinding, releasedBinding])
        };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(new ForegroundApp(1, "Code", null, "x.cs")),
            new StubProfileRepository(profiles));

        var result = await router.RouteAsync(
            new ControllerInputEvent("pad-1", "A", InputGesture.Released, DateTimeOffset.UtcNow));

        Assert.Equal(releasedBinding.Id, result.Binding?.Id);
    }

    [Fact]
    public async Task RouteAsync_HeldEvent_DoesNotFallBackToPressedBinding()
    {
        var binding = CreateBinding("A");
        var profiles = new[]
        {
            CreateProfile("Default", true, 0, [], [binding])
        };
        var router = new ProfileRouter(
            new StubForegroundAppProvider(new ForegroundApp(1, "Code", null, "x.cs")),
            new StubProfileRepository(profiles));

        var result = await router.RouteAsync(
            new ControllerInputEvent("pad-1", "A", InputGesture.Held, DateTimeOffset.UtcNow));

        Assert.Equal(RoutingStatus.NoBinding, result.Status);
    }

    private static InputBinding CreateBinding(string controlId) =>
        new(
            Guid.NewGuid(),
            new ControllerTrigger(controlId, InputGesture.Pressed),
            new KeyboardShortcutAction(["Ctrl", "C"]));

    private static ControllerProfile CreateProfile(
        string name,
        bool isDefault,
        int priority,
        IReadOnlyList<AppMatchRule> rules,
        IReadOnlyList<InputBinding> bindings) =>
        new(Guid.NewGuid(), name, priority, isDefault, rules, bindings);

    private sealed class StubForegroundAppProvider(ForegroundApp? app) : IForegroundAppProvider
    {
        public ValueTask<ForegroundApp?> GetCurrentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(app);
    }

    private sealed class StubProfileRepository(IReadOnlyList<ControllerProfile> profiles)
        : IProfileRepository
    {
        public ValueTask<IReadOnlyList<ControllerProfile>> GetEnabledAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(profiles);
    }
}
