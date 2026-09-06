using System.Runtime.InteropServices;
using ControllerFlow.Core.Models;
using Windows.Gaming.Input;

namespace ControllerFlow.Windows.Input;

/// <summary>统一检查 Windows.Gaming.Input 与 XInput 手柄连接状态。</summary>
public static class GamepadCompatibility
{
    public static int GetConnectedCount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        var xInputCount = XInputNative.GetConnectedCount();
        if (xInputCount > 0)
        {
            return xInputCount;
        }

        try
        {
            return Gamepad.Gamepads.Count;
        }
        catch
        {
            return 0;
        }
    }
}

internal static class XInputGamepadMapper
{
    private const ushort DPadUp = 0x0001;
    private const ushort DPadDown = 0x0002;
    private const ushort DPadLeft = 0x0004;
    private const ushort DPadRight = 0x0008;
    private const ushort Menu = 0x0010;
    private const ushort View = 0x0020;
    private const ushort LeftThumb = 0x0040;
    private const ushort RightThumb = 0x0080;
    private const ushort LeftShoulder = 0x0100;
    private const ushort RightShoulder = 0x0200;
    private const ushort A = 0x1000;
    private const ushort B = 0x2000;
    private const ushort X = 0x4000;
    private const ushort Y = 0x8000;

    public static GamepadFrame BuildFrame(XInputGamepad reading)
    {
        var buttons = new HashSet<string>(StringComparer.Ordinal);
        AddButton(reading.Buttons, A, GamepadControls.A, buttons);
        AddButton(reading.Buttons, B, GamepadControls.B, buttons);
        AddButton(reading.Buttons, X, GamepadControls.X, buttons);
        AddButton(reading.Buttons, Y, GamepadControls.Y, buttons);
        AddButton(reading.Buttons, DPadUp, GamepadControls.DPadUp, buttons);
        AddButton(reading.Buttons, DPadDown, GamepadControls.DPadDown, buttons);
        AddButton(reading.Buttons, DPadLeft, GamepadControls.DPadLeft, buttons);
        AddButton(reading.Buttons, DPadRight, GamepadControls.DPadRight, buttons);
        AddButton(reading.Buttons, LeftShoulder, GamepadControls.LeftBumper, buttons);
        AddButton(reading.Buttons, RightShoulder, GamepadControls.RightBumper, buttons);
        AddButton(reading.Buttons, LeftThumb, GamepadControls.LeftStickClick, buttons);
        AddButton(reading.Buttons, RightThumb, GamepadControls.RightStickClick, buttons);
        AddButton(reading.Buttons, Menu, GamepadControls.Menu, buttons);
        AddButton(reading.Buttons, View, GamepadControls.View, buttons);

        return new GamepadFrame(
            buttons,
            NormalizeThumb(reading.LeftThumbX),
            NormalizeThumb(reading.LeftThumbY),
            NormalizeThumb(reading.RightThumbX),
            NormalizeThumb(reading.RightThumbY),
            reading.LeftTrigger / 255.0,
            reading.RightTrigger / 255.0);
    }

    private static void AddButton(
        ushort buttonsValue,
        ushort buttonMask,
        string controlId,
        ISet<string> buttons)
    {
        if ((buttonsValue & buttonMask) != 0)
        {
            buttons.Add(controlId);
        }
    }

    private static double NormalizeThumb(short value) =>
        value < 0 ? value / 32768.0 : value / 32767.0;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputGamepad
{
    public ushort Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short LeftThumbX;
    public short LeftThumbY;
    public short RightThumbX;
    public short RightThumbY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputState
{
    public uint PacketNumber;
    public XInputGamepad Gamepad;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputVibration
{
    public ushort LeftMotorSpeed;
    public ushort RightMotorSpeed;
}

internal static class XInputNative
{
    private const uint Success = 0;
    private const int MaximumGamepads = 4;

    public static bool TryGetState(uint index, out XInputState state)
    {
        state = default;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return XInputGetState(index, out state) == Success;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static int GetConnectedCount()
    {
        var count = 0;
        for (uint index = 0; index < MaximumGamepads; index++)
        {
            if (TryGetState(index, out _))
            {
                count++;
            }
        }

        return count;
    }

    public static bool TryGetDeviceIndex(string deviceId, out uint index)
    {
        index = 0;
        return deviceId.StartsWith("xinput-", StringComparison.Ordinal)
            && uint.TryParse(deviceId.AsSpan("xinput-".Length), out index)
            && index < MaximumGamepads;
    }

    public static bool TrySetVibration(uint index, ushort leftMotor, ushort rightMotor)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var vibration = new XInputVibration
        {
            LeftMotorSpeed = leftMotor,
            RightMotorSpeed = rightMotor
        };
        try
        {
            return XInputSetState(index, ref vibration) == Success;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputSetState(uint userIndex, ref XInputVibration vibration);
}
