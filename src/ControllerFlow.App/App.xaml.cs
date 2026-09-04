using System.Windows;
using ControllerFlow.Core.Models;
using ControllerFlow.Windows.Logging;

namespace ControllerFlow.App;

public partial class App : Application
{
    public AppServices Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Services = AppServices.Create();
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        await Services.InitializeAsync(CancellationToken.None);

        var mainWindow = new MainWindow(Services);
        MainWindow = mainWindow;

        if (e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            // 开机自启：最小化到托盘。
            mainWindow.StartMinimizedToTray();
        }
        else
        {
            mainWindow.Show();
        }

        // 前台应用监控：解析命中 Profile 并刷新状态栏。
        _ = Task.Run(() => Services.RoutingMonitor.RunAsync(CancellationToken.None));

        // 手柄输入：轮询采样 → 归一化事件 → 引擎路由。
        Services.InputSource.InputReceived += OnControllerInput;
        try
        {
            await Services.InputSource.StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            FileLog.Error("启动手柄输入失败。", ex);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Services.InputSource.InputReceived -= OnControllerInput;
        await Services.ShutdownAsync(CancellationToken.None);
        base.OnExit(e);
    }

    private void OnControllerInput(object? sender, ControllerInputEvent inputEvent)
    {
        // 引擎内部自行捕获并记录执行失败；此处兜底记录未预期异常。
        _ = Services.Engine.HandleAsync(inputEvent, CancellationToken.None)
            .AsTask()
            .ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        FileLog.Error($"处理输入事件失败（{inputEvent.ControlId}/{inputEvent.Gesture}）。", task.Exception);
                    }
                },
                TaskScheduler.Default);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        FileLog.Error("未处理异常。", e.Exception);
        e.Handled = true;
    }
}