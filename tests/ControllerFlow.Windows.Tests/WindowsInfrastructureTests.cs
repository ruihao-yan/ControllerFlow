using ControllerFlow.Core.Models;
using ControllerFlow.Windows.Diagnostics;
using ControllerFlow.Windows.Input;
using ControllerFlow.Windows.Logging;
using Xunit;

namespace ControllerFlow.Windows.Tests;

/// <summary>
/// Windows 层的集成测试。依赖 Windows 运行时（user32.dll / Windows.Gaming.Input）
/// 的常规用例在非 Windows 主机上自动跳过；交互手柄诊断要求真实设备并明确报告失败。
/// </summary>
public sealed class WindowsInfrastructureTests
{
    [Fact]
    public void FileLog_WritesAndReadsLines()
    {
        var path = FileLog.LogFilePath;
        var originalExists = File.Exists(path);
        try
        {
            FileLog.Info("集成测试 INFO 行");
            FileLog.Warn("集成测试 WARN 行");
            FileLog.Error("集成测试 ERROR 行", new InvalidOperationException("附带异常"));

            var content = File.ReadAllText(path);
            Assert.Contains("INFO", content, StringComparison.Ordinal);
            Assert.Contains("集成测试 INFO 行", content, StringComparison.Ordinal);
            Assert.Contains("WARN", content, StringComparison.Ordinal);
            Assert.Contains("ERROR", content, StringComparison.Ordinal);
            Assert.Contains("InvalidOperationException", content, StringComparison.Ordinal);
        }
        finally
        {
            if (!originalExists && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void FileLog_PathIsUnderLocalAppData()
    {
        var expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(expectedRoot, FileLog.LogFilePath, StringComparison.Ordinal);
        Assert.EndsWith("app.log", FileLog.LogFilePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppSelfCheck_ReturnsEveryCheckWithoutThrowing()
    {
        var results = await Task.Run(() => AppSelfCheck.RunAll(
            profileStore: null,
            foregroundAppProvider: new ControllerFlow.Windows.Desktop.Win32ForegroundAppProvider(),
            dataDirectory: Path.Combine(Path.GetTempPath(), "controllerflow-selfcheck")));

        Assert.Equal(5, results.Count);
        Assert.All(results, item => Assert.False(string.IsNullOrWhiteSpace(item.Name)));
        Assert.All(results, item => Assert.False(string.IsNullOrWhiteSpace(item.Detail)));
    }

    [Fact]
    public async Task Win32ForegroundAppProvider_ReturnsForegroundAppOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // 非 Windows CI：跳过（user32.dll 不可用）。
        }

        var provider = new ControllerFlow.Windows.Desktop.Win32ForegroundAppProvider();
        var app = await provider.GetCurrentAsync(CancellationToken.None);

        // 桌面会话中前台窗口通常存在；无窗口会话（如服务）则允许为 null。
        if (app is not null)
        {
            Assert.True(app.ProcessId > 0);
            Assert.False(string.IsNullOrWhiteSpace(app.ProcessName));
        }
    }

    [Fact]
    public void XInputGamepadMapper_MapsRightShoulderToRb()
    {
        var frame = XInputGamepadMapper.BuildFrame(new XInputGamepad
        {
            Buttons = 0x0200
        });

        Assert.Contains(GamepadControls.RightBumper, frame.PressedButtons);
    }

    [Fact]
    public void XInputGamepadMapper_MapsFaceButtonsToAAndB()
    {
        var frame = XInputGamepadMapper.BuildFrame(new XInputGamepad
        {
            Buttons = 0x1000 | 0x2000
        });

        Assert.Contains(GamepadControls.A, frame.PressedButtons);
        Assert.Contains(GamepadControls.B, frame.PressedButtons);
    }

    [Fact]
    public void XInputGamepadMapper_NormalizesAxesAndTriggers()
    {
        var frame = XInputGamepadMapper.BuildFrame(new XInputGamepad
        {
            LeftTrigger = byte.MaxValue,
            RightTrigger = 128,
            LeftThumbX = short.MinValue,
            LeftThumbY = short.MaxValue
        });

        Assert.Equal(1, frame.LeftTrigger);
        Assert.Equal(128 / 255.0, frame.RightTrigger);
        Assert.Equal(-1, frame.LeftThumbX);
        Assert.Equal(1, frame.LeftThumbY);
    }

    [Fact]
    public void TrayIcon_MessageConstantsAreStable()
    {
        // 跨平台可验证的常量契约（Windows 侧绑定 user32 消息号）。
        Assert.Equal(0x8001u, ControllerFlow.Windows.Desktop.TrayIcon.CallbackMessage);
        if (OperatingSystem.IsWindows())
        {
            Assert.NotEqual(0u, ControllerFlow.Windows.Desktop.TrayIcon.TaskbarCreatedMessage);
        }
    }
}