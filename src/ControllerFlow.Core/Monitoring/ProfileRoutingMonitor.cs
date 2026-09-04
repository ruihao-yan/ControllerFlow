using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;
using ControllerFlow.Core.Routing;

namespace ControllerFlow.Core.Monitoring;

/// <summary>前台应用变化后重新解析 Profile 的事件参数。</summary>
public sealed class ProfileResolutionChangedEventArgs(
    ForegroundApp? App,
    ProfileResolution Resolution) : EventArgs
{
    public ForegroundApp? App { get; } = App;

    public ProfileResolution Resolution { get; } = Resolution;
}

/// <summary>
/// 前台路由监控：复用 <see cref="ForegroundAppMonitor"/> 轮询前台应用，
/// 每次变化时通过 <see cref="ProfileResolver"/> 重新解析当前 Profile，
/// 并触发 <see cref="ResolutionChanged"/> 供 UI 展示“当前前台 → 命中 Profile”。
/// 单次解析失败只跳过本次通知，不终止轮询。
/// </summary>
public sealed class ProfileRoutingMonitor
{
    private readonly ProfileResolver _resolver;
    private readonly ForegroundAppMonitor _appMonitor;

    public ProfileRoutingMonitor(
        IForegroundAppProvider foregroundAppProvider,
        IProfileRepository profileRepository,
        TimeSpan? pollInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(foregroundAppProvider);
        ArgumentNullException.ThrowIfNull(profileRepository);
        _resolver = new ProfileResolver(profileRepository);
        _appMonitor = new ForegroundAppMonitor(
            foregroundAppProvider,
            pollInterval,
            delay,
            OnAppChangedAsync);
    }

    /// <summary>前台应用变化且重新解析完成时触发（含首次探测到前台应用时）。</summary>
    public event EventHandler<ProfileResolutionChangedEventArgs>? ResolutionChanged;

    /// <summary>当前前台应用（从未探测到时为 null）。</summary>
    public ForegroundApp? CurrentApp => _appMonitor.Current;

    /// <summary>最近一次解析结果（尚未解析过时为 null）。</summary>
    public ProfileResolution? CurrentResolution { get; private set; }

    /// <summary>开始轮询，直到 <paramref name="cancellationToken"/> 被取消。</summary>
    public Task RunAsync(CancellationToken cancellationToken) =>
        _appMonitor.RunAsync(cancellationToken);

    private async Task OnAppChangedAsync(
        ForegroundApp? previous,
        ForegroundApp? current,
        CancellationToken cancellationToken)
    {
        var resolution = await _resolver.ResolveAsync(current, cancellationToken);
        CurrentResolution = resolution;
        ResolutionChanged?.Invoke(
            this,
            new ProfileResolutionChangedEventArgs(current, resolution));
    }
}