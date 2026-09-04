using Microsoft.Win32;

namespace ControllerFlow.Windows;

/// <summary>
/// 开机自启管理：写入 / 移除 HKCU Run 键（无需管理员权限）。
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ControllerFlow";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>启用 / 禁用开机自启（以最小化到托盘方式启动）。</summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return;
            }

            key.SetValue(ValueName, $"\"{executable}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
