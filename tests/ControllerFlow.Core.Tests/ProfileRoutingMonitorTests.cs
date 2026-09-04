using System.Collections.Concurrent;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Monitoring;
using ControllerFlow.Core.Routing;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class ProfileRoutingMonitorTests
{
    private static readonly ForegroundApp Chrome = new(1, "chrome", null, "YouTube");
    private static readonly ForegroundApp Notepad = new(2, "notepad", null, "笔记.txt");

    [Fact]
    public async Task RunAsync_ResolvesProfileOnAppChange()
    {
        var call = 0;
        var provider = new ScriptedForegroundAppProvider(() => (call++) switch
        {
            0 => Chrome,
            _ => Notepad
        });
        var profiles = new[]
        {
            TestProfiles.AppProfile("浏览器配置", new AppMatchRule(ProcessName: "chrome")),
            TestProfiles.DefaultProfile()
        };
        var events = new ConcurrentQueue<ProfileResolutionChangedEventArgs>();
        var monitor = new ProfileRoutingMonitor(
            provider,
            new StubProfileRepository(profiles),
            pollInterval: TimeSpan.Zero,
            delay: ImmediateDelay);
        monitor.ResolutionChanged += (_, e) => events.Enqueue(e);

        using var cts = new CancellationTokenSource();
        var runTask = monitor.RunAsync(cts.Token);
        await TestWait.UntilAsync(() => events.Count >= 2, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;

        var all = events.ToArray();
        Assert.Equal(Chrome, all[0].App);
        Assert.Equal("浏览器配置", all[0].Resolution.Profile?.Name);
        Assert.False(all[0].Resolution.UsedDefaultFallback);

        Assert.Equal(Notepad, all[1].App);
        Assert.True(all[1].Resolution.UsedDefaultFallback);
        Assert.True(all[1].Resolution.Profile?.IsDefault);

        Assert.Equal(Notepad, monitor.CurrentApp);
        Assert.Equal("默认", monitor.CurrentResolution!.Profile?.Name);
    }

    [Fact]
    public async Task RunAsync_NoProfiles_ResolutionIsNull()
    {
        var provider = new ScriptedForegroundAppProvider(() => Chrome);
        var events = new ConcurrentQueue<ProfileResolutionChangedEventArgs>();
        var monitor = new ProfileRoutingMonitor(
            provider,
            new StubProfileRepository([]),
            pollInterval: TimeSpan.Zero,
            delay: ImmediateDelay);
        monitor.ResolutionChanged += (_, e) => events.Enqueue(e);

        using var cts = new CancellationTokenSource();
        var runTask = monitor.RunAsync(cts.Token);
        await TestWait.UntilAsync(() => events.Count >= 1, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;

        var evt = Assert.Single(events.ToArray());
        Assert.Null(evt.Resolution.Profile);
    }

    private static Task ImmediateDelay(TimeSpan _, CancellationToken _2) => Task.Delay(1);
}