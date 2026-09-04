using System.Diagnostics;
using System.Runtime.InteropServices;
using ControllerFlow.Core.Input;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;

namespace ControllerFlow.Windows.Output;

/// <summary>
/// 基于 SendInput / Process.Start 的输出执行器：
/// 键盘组合键（含媒体键，KeyDownOnly / KeyUpOnly 语义由引擎配对）、
/// 鼠标相对移动与滚轮、启动程序。语音动作由引擎拆解为
/// Start / Stop 快捷键后逐次调用本执行器。
/// </summary>
public sealed class Win32ActionExecutor : IActionExecutor
{
    public async ValueTask ExecuteAsync(
        OutputAction action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action)
        {
            case KeyboardShortcutAction keyboard:
                ExecuteKeyboard(keyboard);
                break;

            case MouseAction mouse:
                ExecuteMouse(mouse);
                break;

            case MediaKeyAction media:
                SendKeyDown((ushort)media.Key);
                SendKeyUp((ushort)media.Key);
                break;

            case LaunchApplicationAction launch:
                await ExecuteLaunchAsync(launch, cancellationToken);
                break;

            case SpeechToolAction speech:
                // 语音会话由引擎按按下/释放拆解；此处兜底仅做一次“开始”点按。
                ExecuteKeyboard(speech.Start with { KeyDownOnly = false, KeyUpOnly = false });
                break;

            default:
                throw new NotSupportedException($"不支持的动作类型：{action.GetType().Name}");
        }
    }

    private static void ExecuteKeyboard(KeyboardShortcutAction action)
    {
        var keys = action.Keys.Select(ResolveKey).ToArray();
        if (keys.Length == 0)
        {
            return;
        }

        if (!action.KeyUpOnly)
        {
            foreach (var key in keys)
            {
                SendKeyDown((ushort)key);
            }
        }

        if (!action.KeyDownOnly)
        {
            for (var i = keys.Length - 1; i >= 0; i--)
            {
                SendKeyUp((ushort)keys[i]);
            }
        }
    }

    private static void ExecuteMouse(MouseAction action)
    {
        switch (action.Operation)
        {
            case MouseOperation.LeftClick:
                Click(MouseEventFlags.LeftDown, MouseEventFlags.LeftUp);
                break;

            case MouseOperation.RightClick:
                Click(MouseEventFlags.RightDown, MouseEventFlags.RightUp);
                break;

            case MouseOperation.MiddleClick:
                Click(MouseEventFlags.MiddleDown, MouseEventFlags.MiddleUp);
                break;

            case MouseOperation.ScrollVertical:
                SendMouseInput(new MouseInput
                {
                    MouseData = unchecked((uint)action.Amount),
                    Flags = (uint)MouseEventFlags.Wheel
                });
                break;

            case MouseOperation.ScrollHorizontal:
                SendMouseInput(new MouseInput
                {
                    MouseData = unchecked((uint)action.Amount),
                    Flags = (uint)MouseEventFlags.HorizontalWheel
                });
                break;

            case MouseOperation.Move:
                // 相对移动：按住重复时逐次累加，适合摇杆驱动的光标移动。
                SendMouseInput(new MouseInput
                {
                    X = action.Amount,
                    Flags = (uint)MouseEventFlags.Move
                });
                break;

            default:
                throw new NotSupportedException($"不支持的鼠标操作：{action.Operation}");
        }
    }

    private static async ValueTask ExecuteLaunchAsync(
        LaunchApplicationAction action,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = action.ExecutablePath,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(action.Arguments))
        {
            startInfo.Arguments = action.Arguments;
        }

        await Task.Run(() => Process.Start(startInfo), cancellationToken);
    }

    private static void Click(MouseEventFlags downFlag, MouseEventFlags upFlag)
    {
        SendMouseInput(new MouseInput { Flags = (uint)downFlag });
        SendMouseInput(new MouseInput { Flags = (uint)upFlag });
    }

    private static KeyCode ResolveKey(string name) =>
        KeyNameMap.TryGet(name, out var code)
            ? code
            : throw new InvalidOperationException($"无法识别的按键名：{name}");

    private static void SendKeyDown(ushort virtualKey) =>
        SendKeyboardInput(virtualKey, 0);

    private static void SendKeyUp(ushort virtualKey) =>
        SendKeyboardInput(virtualKey, KeyEventFlags.KeyUp);

    private static void SendKeyboardInput(ushort virtualKey, KeyEventFlags flags)
    {
        var input = new Input
        {
            Type = (uint)InputType.Keyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = (uint)flags
                }
            }
        };
        _ = NativeMethods.SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private static void SendMouseInput(MouseInput mouse)
    {
        var input = new Input
        {
            Type = (uint)InputType.Mouse,
            Data = new InputUnion { Mouse = mouse }
        };
        _ = NativeMethods.SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private enum InputType : uint
    {
        Mouse = 0,
        Keyboard = 1
    }

    [Flags]
    private enum KeyEventFlags : uint
    {
        KeyUp = 0x0002
    }

    [Flags]
    private enum MouseEventFlags : uint
    {
        Move = 0x0001,
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010,
        MiddleDown = 0x0020,
        MiddleUp = 0x0040,
        Wheel = 0x0800,
        HorizontalWheel = 0x1000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, Input[] inputs, int size);
    }
}
