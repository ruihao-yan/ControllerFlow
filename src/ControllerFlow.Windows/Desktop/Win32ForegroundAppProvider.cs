using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;

namespace ControllerFlow.Windows.Desktop;

/// <summary>
/// 通过 Win32 API 获取当前前台应用：前台窗口句柄 -> 进程 ID
/// -> 可执行文件完整路径 -> 进程名与窗口标题。
/// </summary>
public sealed class Win32ForegroundAppProvider : IForegroundAppProvider
{
    public ValueTask<ForegroundApp?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return ValueTask.FromResult<ForegroundApp?>(null);
        }

        _ = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return ValueTask.FromResult<ForegroundApp?>(null);
        }

        var path = GetExecutablePath((int)processId);
        var name = GetProcessName(path, (int)processId);
        return ValueTask.FromResult<ForegroundApp?>(
            new ForegroundApp((int)processId, name, path, GetWindowTitle(window)));
    }

    private static string? GetExecutablePath(int processId)
    {
        try
        {
            var handle = NativeMethods.OpenProcess(
                NativeMethods.ProcessQueryLimitedInformation,
                false,
                (uint)processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var builder = new StringBuilder(1024);
                var size = (uint)builder.Capacity;
                return NativeMethods.QueryFullProcessImageName(handle, 0, builder, ref size)
                    ? builder.ToString()
                    : null;
            }
            finally
            {
                _ = NativeMethods.CloseHandle(handle);
            }
        }
        catch
        {
            return null;
        }
    }

    private static string GetProcessName(string? path, int processId)
    {
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                return Path.GetFileNameWithoutExtension(path);
            }
            catch
            {
                // 路径异常时退回进程名。
            }
        }

        try
        {
            return Process.GetProcessById(processId).ProcessName;
        }
        catch
        {
            return $"pid-{processId}";
        }
    }

    private static string GetWindowTitle(IntPtr window)
    {
        var builder = new StringBuilder(512);
        _ = NativeMethods.GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }
}

internal static class NativeMethods
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        IntPtr process,
        uint flags,
        StringBuilder imageName,
        ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}
