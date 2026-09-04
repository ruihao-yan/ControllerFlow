using System.Runtime.InteropServices;

namespace ControllerFlow.Windows.Desktop;

/// <summary>
/// 系统托盘图标的 Win32 封装（Shell_NotifyIcon），不依赖 WPF。
/// 宿主（WPF 应用）提供窗口句柄并监听 <see cref="CallbackMessage"/>;
/// 通过 <see cref="TaskbarCreatedMessage"/> 在资源管理器重启后重新注册。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    /// <summary>托盘回调消息（WM_APP + 1，宿主窗口在此消息中接收鼠标事件）。</summary>
    public const uint CallbackMessage = 0x8001;

    /// <summary>资源管理器重启（TaskbarCreated）消息；宿主应在收到后调用 <see cref="ReAdd"/>。</summary>
    public static uint TaskbarCreatedMessage { get; } = RegisterTaskbarCreatedMessage();

    private const int NimAdd = 0x00000000;
    private const int NimModify = 0x00000001;
    private const int NimDelete = 0x00000002;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;

    private const uint NiiifInfo = 0x00000001;

    private readonly IntPtr _windowHandle;
    private readonly uint _iconId;
    private bool _added;

    public TrayIcon(IntPtr windowHandle, uint iconId = 1)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("托盘图标需要有效的窗口句柄。", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _iconId = iconId;
    }

    /// <summary>添加托盘图标（重复调用先移除再添加）。</summary>
    public bool Add(IntPtr iconHandle, string tooltip)
    {
        if (_added)
        {
            _ = Remove();
        }

        _added = NativeMethods.Shell_NotifyIcon(
            NimAdd,
            BuildData(iconHandle, tooltip));
        return _added;
    }

    /// <summary>资源管理器重启后重新注册（收到 TaskbarCreated 消息时调用）。</summary>
    public bool ReAdd(IntPtr iconHandle, string tooltip) => Add(iconHandle, tooltip);

    /// <summary>更新图标与悬停提示。</summary>
    public bool Update(IntPtr iconHandle, string tooltip) =>
        NativeMethods.Shell_NotifyIcon(NimModify, BuildData(iconHandle, tooltip));

    /// <summary>显示气泡通知。</summary>
    public bool ShowBalloon(string title, string text)
    {
        var data = BuildData(IntPtr.Zero, string.Empty);
        data.Flags = NifInfo;
        data.InfoTitle = Truncate(title ?? string.Empty, 63);
        data.Info = Truncate(text ?? string.Empty, 255);
        data.InfoFlags = NiiifInfo;
        return NativeMethods.Shell_NotifyIcon(NimModify, data);
    }

    /// <summary>移除托盘图标。</summary>
    public bool Remove()
    {
        if (!_added)
        {
            return false;
        }

        _added = false;
        return NativeMethods.Shell_NotifyIcon(
            NimDelete,
            BuildData(IntPtr.Zero, string.Empty));
    }

    public void Dispose() => Remove();

    private NotifyIconData BuildData(IntPtr iconHandle, string tooltip)
    {
        var data = new NotifyIconData
        {
            CbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            Hwnd = _windowHandle,
            Id = _iconId,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = CallbackMessage,
            Icon = iconHandle,
            ToolTip = Truncate(tooltip ?? string.Empty, 127)
        };
        return data;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static uint RegisterTaskbarCreatedMessage() =>
        NativeMethods.RegisterWindowMessage("TaskbarCreated");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint CbSize;
        public IntPtr Hwnd;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ToolTip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Shell_NotifyIcon(int command, NotifyIconData data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint RegisterWindowMessage(string message);
    }
}