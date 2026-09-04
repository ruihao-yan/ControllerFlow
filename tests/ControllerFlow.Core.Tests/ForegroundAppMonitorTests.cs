using System.Collections.Concurrent;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Monitoring;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class ForegroundAppMonitorTests
{
    [Fact]
    public async Task RunAsync_RaisesOnChange_NotOnSame()
    {
        var call = 0;
        var provider = new ScriptedForegroundAppProvider(() => (call++) switch
        {
            0 => Chrome,
            1 => Chrome,
            2 => Notepad,
            _ => null
        });

        var changed = new ConcurrentQueue<ForegroundAppChangedEventArgs>();
        var monitor = new ForegroundAppMonitor(provider, pollInterval: TimeSpan.Zero, delay: ImmediateDelay);
        monitor.AppChanged += (_, e) => changed.Enqueue(e);

        using var cts = new CancellationTokenSource();
        var runTask = monitor.RunAsync(cts.Token);
        await TestWait.UntilAsync(() => provider.Calls >= 4, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;

        // null→Chrome, Chrome→Notepad, Notepad→null：3 次变化（Chrome→Chrome 不触发）。
        var events = changed.ToArray();
        Assert.Equal(3, events.Length);
        Assert.Equal(Chrome, events[0].Current);
        Assert.Equal(Notepad, events[1].Current);
        Assert.Null(events[2].Current);
        Assert.Equal(Chrome, events[1].Previous);
        Assert.Null(monitor.Current);
    }

    [Fact]
    public async Task RunAsync_ProviderException_DoesNotStopLoop()
    {
        var failures = 0;
        var provider = new ScriptedForegroundAppProvider(() =>
        {
            if (failures++ == 0)
            {
                throw new InvalidOperationException("探测失败");
            }

            return Chrome;
        });

        var changed = 0;
        var monitor = new ForegroundAppMonitor(provider, pollInterval: TimeSpan.Zero, delay: ImmediateDelay);
        monitor.AppChanged += (_, _) => Interlocked.Increment(ref changed);

        using var cts = new CancellationTokenSource();
        var runTask = monitor.RunAsync(cts.Token);
        await TestWait.UntilAsync(() => Volatile.Read(ref changed) >= 1, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;

        Assert.Equal(1, Volatile.Read(ref changed));
        Assert.Equal(Chrome, monitor.Current);
    }

    [Fact]
    public async Task RunAsync_Cancellation_CompletesCleanly()
    {
        var provider = new ScriptedForegroundAppProvider(() => Chrome);
        var monitor = new ForegroundAppMonitor(provider, pollInterval: TimeSpan.Zero, delay: ImmediateDelay);

        using var cts = new CancellationTokenSource();
        var runTask = monitor.RunAsync(cts.Token);
        await TestWait.UntilAsync(() => provider.Calls >= 2, TimeSpan.FromSeconds(5));
        cts.Cancel();

        await runTask; // 不应抛出。
    }

    [Fact]
    public async Task RunAsync_AsyncCallback_InvokedOnChange()
    {
        var call = 0;
        var provider = new ScriptedForegroundAppProvider(() => (call++) switch
        {
            0 => Chrome,
            _ => Notepad
        });

        var callbacks = new ConcurrentQueue<(ForegroundApp? Previous, ForegroundApp? Current)>();
        var monitor = new ForegroundAppMonitor(
            provider,
            pollInterval: TimeSpan.Zero,
            delay: ImmediateDelay,
            onAppChanged: async (previous, current, _) =>
            {
                await Task.Yield();
                callbacks.Enqueue((previous, current));
            });

        using var cts = new CancellationTokenSource();
        var runTask = monitor.RunAsync(cts.Token);
        await TestWait.UntilAsync(() => callbacks.Count >= 2, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;

        var events = callbacks.ToArray();
        Assert.Equal(2, events.Length);
        Assert.Null(events[0].Previous);
        Assert.Equal(Chrome, events[0].Current);
        Assert.Equal(Chrome, events[1].Previous);
        Assert.Equal(Notepad, events[1].Current);
    }

    [Fact]
    public async Task RunAsync_CallbackException_DoesNotStopLoop()
    {
        var call = 0;
        // 交替返回不同前台应用，保证回调每次轮询都会触发。
        var provider = new ScriptedForegroundAppProvider(() => (call++ % 2 == 0) ? Chrome : Notepad);
        var calls = 0;
        var monitor = new ForegroundAppMonitor(
            provider,
            pollInterval: TimeSpan.Zero,
            delay: ImmediateDelay,
            onAppChanged: (_, _, _) =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException("解析失败");
            });

        using var cts = new CancellationTokenSource();
        var runTask = monitor.RunAsync(cts.Token);
        await TestWait.UntilAsync(() => Volatile.Read(ref calls) >= 2, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;
    }

    private static Task ImmediateDelay(TimeSpan _, CancellationToken _2) => Task.Delay(1);

    private static readonly ForegroundApp Chrome = new(1, "chrome", null, "YouTube");
    private static readonly ForegroundApp Notepad = new(2, "notepad", null, "笔记.txt");
}