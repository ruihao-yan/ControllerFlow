using ControllerFlow.Core.Ports;
using Windows.Gaming.Input;

namespace ControllerFlow.Windows.Diagnostics;

/// <summary>单项自检结果。</summary>
public sealed record SelfCheckItem(string Name, bool Passed, string Detail);

/// <summary>
/// 应用自检：手柄 API 可用性、已连接手柄、Profile 文件可读、
/// 前台窗口探测、数据目录可写。任何单项失败都不应中断其余检查。
/// </summary>
public static class AppSelfCheck
{
    public static IReadOnlyList<SelfCheckItem> RunAll(
        IProfileStore? profileStore = null,
        IForegroundAppProvider? foregroundAppProvider = null,
        string? dataDirectory = null) =>
    [
        CheckGamepadApi(),
        CheckConnectedGamepads(),
        CheckProfileStore(profileStore),
        CheckForegroundAppProvider(foregroundAppProvider),
        CheckDataDirectory(dataDirectory)
    ];

    private static SelfCheckItem CheckGamepadApi()
    {
        if (!OperatingSystem.IsWindows())
        {
            // 不加载 Windows.Gaming.Input 投影：避免非 Windows 主机上运行时崩溃。
            return new SelfCheckItem("手柄 API (Windows.Gaming.Input)", true, "非 Windows 平台，跳过");
        }

        try
        {
            _ = Gamepad.Gamepads.Count;
            return new SelfCheckItem("手柄 API (Windows.Gaming.Input)", true, "可用");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("手柄 API (Windows.Gaming.Input)", false, $"不可用：{ex.Message}");
        }
    }

    private static SelfCheckItem CheckConnectedGamepads()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SelfCheckItem("已连接手柄", true, "非 Windows 平台，跳过");
        }

        try
        {
            var count = Gamepad.Gamepads.Count;
            return count > 0
                ? new SelfCheckItem("已连接手柄", true, $"检测到 {count} 只手柄")
                : new SelfCheckItem("已连接手柄", true, "未检测到手柄（连接手柄后自动上线）");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("已连接手柄", false, $"读取失败：{ex.Message}");
        }
    }

    private static SelfCheckItem CheckProfileStore(IProfileStore? profileStore)
    {
        if (profileStore is null)
        {
            return new SelfCheckItem("Profile 配置", true, "未提供存储实例，跳过");
        }

        try
        {
            var profiles = profileStore.LoadAsync().AsTask().GetAwaiter().GetResult();
            return new SelfCheckItem("Profile 配置", true, $"可读取，共 {profiles.Count} 个 Profile");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("Profile 配置", false, $"读取失败：{ex.Message}");
        }
    }

    private static SelfCheckItem CheckForegroundAppProvider(IForegroundAppProvider? provider)
    {
        if (provider is null)
        {
            return new SelfCheckItem("前台窗口探测", true, "未提供探测实例，跳过");
        }

        try
        {
            var app = provider.GetCurrentAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            return app is null
                ? new SelfCheckItem("前台窗口探测", true, "当前无前台窗口")
                : new SelfCheckItem("前台窗口探测", true, $"当前前台：{app.ProcessName}（{app.WindowTitle}）");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("前台窗口探测", false, $"探测失败：{ex.Message}");
        }
    }

    private static SelfCheckItem CheckDataDirectory(string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return new SelfCheckItem("数据目录", true, "未提供路径，跳过");
        }

        try
        {
            Directory.CreateDirectory(dataDirectory);
            var probe = Path.Combine(dataDirectory, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return new SelfCheckItem("数据目录", true, $"可写：{dataDirectory}");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("数据目录", false, $"不可写：{ex.Message}");
        }
    }
}