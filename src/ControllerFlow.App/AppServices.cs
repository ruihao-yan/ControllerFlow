using ControllerFlow.Core.Engine;
using ControllerFlow.Core.Input;
using ControllerFlow.Core.Monitoring;
using ControllerFlow.Core.Ports;
using ControllerFlow.Core.Profiles;
using ControllerFlow.Core.Routing;
using ControllerFlow.Windows.Desktop;
using ControllerFlow.Windows.Haptics;
using ControllerFlow.Windows.Input;
using ControllerFlow.Windows.Logging;
using ControllerFlow.Windows.Output;
using System.IO;

namespace ControllerFlow.App;

/// <summary>
/// 组合根：装配 Core 路由链路与 Windows 适配层的全部服务。
/// App 只做组装与 UI，不包含业务逻辑。
/// </summary>
public sealed class AppServices
{
    public required string DataDirectory { get; init; }

    public required string ProfilesFilePath { get; init; }

    public required IProfileStore Store { get; init; }

    public required ProfileEditorService Editor { get; init; }

    public required ProfileStoreRepository Repository { get; init; }

    public required Win32ForegroundAppProvider ForegroundProvider { get; init; }

    public required ProfileRoutingMonitor RoutingMonitor { get; init; }

    public required WindowsGamepadSource InputSource { get; init; }

    public required RoutingEngine Engine { get; init; }

    public required Win32ActionExecutor Executor { get; init; }

    public static AppServices Create()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ControllerFlow");
        Directory.CreateDirectory(dataDirectory);

        var store = new JsonProfileStore(Path.Combine(dataDirectory, "profiles.json"));
        var repository = new ProfileStoreRepository(store);
        var foregroundProvider = new Win32ForegroundAppProvider();
        var router = new ProfileRouter(foregroundProvider, repository);
        var executor = new Win32ActionExecutor();
        var engine = new RoutingEngine(
            router,
            executor,
            haptic: new GamepadHapticFeedback(),
            options: RoutingEngineOptions.Default,
            timeProvider: TimeProvider.System);

        return new AppServices
        {
            DataDirectory = dataDirectory,
            ProfilesFilePath = Path.Combine(dataDirectory, "profiles.json"),
            Store = store,
            Editor = new ProfileEditorService(store),
            Repository = repository,
            ForegroundProvider = foregroundProvider,
            RoutingMonitor = new ProfileRoutingMonitor(
                foregroundProvider,
                repository,
                pollInterval: TimeSpan.FromMilliseconds(500)),
            InputSource = new WindowsGamepadSource(GamepadInputTrackerOptions.Default),
            Engine = engine,
            Executor = executor
        };
    }

    /// <summary>启动时从存储加载 Profile 并记录日志；损坏时记录但不崩溃（编辑窗口会显示空列表）。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        FileLog.Info("ControllerFlow 启动。");
        try
        {
            await Repository.ReloadAsync(cancellationToken);
            var profiles = await Repository.GetAllAsync(cancellationToken);
            FileLog.Info($"已加载 {profiles.Count} 个 Profile（{ProfilesFilePath}）。");
        }
        catch (Exception ex)
        {
            FileLog.Error("加载 Profile 失败，将继续以空配置启动。", ex);
        }
    }

    /// <summary>停止输入源并释放引擎保持的按键，避免组合键卡住。</summary>
    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        FileLog.Info("ControllerFlow 退出中。");
        try
        {
            await Engine.ReleaseAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            FileLog.Error("释放引擎状态失败。", ex);
        }

        try
        {
            await InputSource.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            FileLog.Error("停止输入源失败。", ex);
        }

        FileLog.Info("ControllerFlow 已退出。");
    }
}