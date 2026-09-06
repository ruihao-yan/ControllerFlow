using System.Diagnostics;
using System.Runtime.InteropServices;
using ControllerFlow.Core.Input;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;

namespace ControllerFlow.Windows.Output;

/// <summary>
/// 基于 SendInput / Process.Start 的输出执行器：
/// 键盘组合键（含媒体键，KeyDownOnly / KeyUpOnly 语义由引擎配对）、
/// 鼠标相对移动与滚轮、启动程序。
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
                SendKeyboardInput((ushort)media.Key, keyUp: false);
                SendKeyboardInput((ushort)media.Key, keyUp: true);
                break;

            case LaunchApplicationAction launch:
                await ExecuteLaunchAsync(launch, cancellationToken);
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
                SendKeyboardInput((ushort)key, keyUp: false);
            }
        }

        if (!action.KeyDownOnly)
        {
            for (var i = keys.Length - 1; i >= 0; i--)
            {
                SendKeyboardInput((ushort)keys[i], keyUp: true);
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

    private static void SendKeyboardInput(ushort virtualKey, bool keyUp)
    {
        var flags = keyUp ? KeyEventFlags.KeyUp : 0;
        if (IsExtendedKey(virtualKey))
        {
            flags |= KeyEventFlags.ExtendedKey;
        }

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
        var sent = NativeMethods.SendInput(1, [input], Marshal.SizeOf<Input>());
        if (sent != 1)
        {
            throw new InvalidOperationException(
                $"键盘输入注入失败，SendInput 返回 {sent}，错误码 {Marshal.GetLastWin32Error()}。");
        }
    }

    private static bool IsExtendedKey(ushort virtualKey) => virtualKey switch
    {
        (ushort)KeyCode.Insert or
        (ushort)KeyCode.Delete or
        (ushort)KeyCode.Home or
        (ushort)KeyCode.End or
        (ushort)KeyCode.PageUp or
        (ushort)KeyCode.PageDown or
        (ushort)KeyCode.Left or
        (ushort)KeyCode.Up or
        (ushort)KeyCode.Right or
        (ushort)KeyCode.Down or
        (ushort)KeyCode.PrintScreen or
        (ushort)KeyCode.Divide or
        (ushort)KeyCode.RightControl or
        (ushort)KeyCode.RightAlt or
        (ushort)KeyCode.LeftWindows or
        (ushort)KeyCode.RightWindows => true,
        _ => false
    };

    private static void SendMouseInput(MouseInput mouse)
    {
        var input = new Input
        {
            Type = (uint)InputType.Mouse,
            Data = new InputUnion { Mouse = mouse }
        };
        var sent = NativeMethods.SendInput(1, [input], Marshal.SizeOf<Input>());
        if (sent != 1)
        {
            throw new InvalidOperationException(
                $"鼠标输入注入失败，SendInput 返回 {sent}，错误码 {Marshal.GetLastWin32Error()}。");
        }
    }

    private enum InputType : uint
    {
        Mouse = 0,
        Keyboard = 1
    }

    [Flags]
    private enum KeyEventFlags : uint
    {
        ExtendedKey = 0x0001,
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
