using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;

namespace ControllerFlow.Core.Monitoring;

/// <summary>前台应用变化事件参数。</summary>
public sealed class ForegroundAppChangedEventArgs(
    ForegroundApp? Previous,
    ForegroundApp? Current) : EventArgs
{
    public ForegroundApp? Previous { get; } = Previous;

    public ForegroundApp? Current { get; } = Current;
}

/// <summary>
/// 轮询 <see cref="IForegroundAppProvider"/>，在前台应用变化时触发
/// <see cref="AppChanged"/> 事件并维护 <see cref="Current"/>。
/// </summary>
public sealed class ForegroundAppMonitor
{
    private readonly IForegroundAppProvider _provider;
    private readonly TimeSpan _pollInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<ForegroundApp?, ForegroundApp?, CancellationToken, Task>? _onAppChanged;

    public ForegroundAppMonitor(
        IForegroundAppProvider provider,
        TimeSpan? pollInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<ForegroundApp?, ForegroundApp?, CancellationToken, Task>? onAppChanged = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        _delay = delay ?? Task.Delay;
        _onAppChanged = onAppChanged;
    }

    public event EventHandler<ForegroundAppChangedEventArgs>? AppChanged;

    public ForegroundApp? Current { get; private set; }

    /// <summary>开始轮询，直到 <paramref name="cancellationToken"/> 被取消。</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var app = await _provider.GetCurrentAsync(cancellationToken);
                var previous = Current;
                Current = app;
                if (!Equals(previous, app))
                {
                    AppChanged?.Invoke(this, new ForegroundAppChangedEventArgs(previous, app));
                    if (_onAppChanged is not null)
                    {
                        await _onAppChanged(previous, app, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 单次探测失败（如窗口句柄失效）不应终止监控循环。
            }

            try
            {
                await _delay(_pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
