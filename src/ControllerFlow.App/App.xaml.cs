using System.Windows;
using ControllerFlow.Core.Models;
using ControllerFlow.Windows.Logging;

namespace ControllerFlow.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\ControllerFlow.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private bool _servicesStarted;

    public AppServices Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Services = AppServices.Create();
        _servicesStarted = true;
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
        if (_servicesStarted)
        {
            Services.InputSource.InputReceived -= OnControllerInput;
            await Services.ShutdownAsync(CancellationToken.None);
        }

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private async void OnControllerInput(object? sender, ControllerInputEvent inputEvent)
    {
        try
        {
            var outcome = await Services.Engine.HandleAsync(inputEvent, CancellationToken.None);
            if (outcome.ActionExecuted)
            {
                FileLog.Info(
                    $"输入已执行：{inputEvent.DeviceId}/{inputEvent.ControlId}/{inputEvent.Gesture}，Profile={outcome.Profile?.Name ?? "无"}。");
            }
            else if (!string.IsNullOrWhiteSpace(outcome.Error))
            {
                FileLog.Warn(
                    $"输入执行失败：{inputEvent.DeviceId}/{inputEvent.ControlId}/{inputEvent.Gesture}，{outcome.Error}");
            }
            else
            {
                FileLog.Info(
                    $"输入未执行：{inputEvent.DeviceId}/{inputEvent.ControlId}/{inputEvent.Gesture}，状态={outcome.Status}，Profile={outcome.Profile?.Name ?? "无"}。");
            }
        }
        catch (Exception ex)
        {
            FileLog.Error($"处理输入事件失败（{inputEvent.ControlId}/{inputEvent.Gesture}）。", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        FileLog.Error("未处理异常。", e.Exception);
        e.Handled = true;
    }
}