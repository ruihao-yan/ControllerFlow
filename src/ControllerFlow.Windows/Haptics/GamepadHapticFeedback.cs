using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;
using ControllerFlow.Windows.Input;
using Windows.Gaming.Input;

namespace ControllerFlow.Windows.Haptics;

/// <summary>
/// 基于 Windows.Gaming.Input 与 XInput 的手柄震动反馈。
/// 通过 <see cref="GamepadRegistry"/> 或 XInput 设备 ID 定位发出事件的手柄；
/// 手柄已断开时回退到任一已连接手柄。
/// </summary>
public sealed class GamepadHapticFeedback : IHapticFeedback
{
    public async ValueTask PlayAsync(
        string deviceId,
        HapticPattern pattern,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (pattern.Duration <= TimeSpan.Zero)
        {
            return;
        }

        if (pattern.LeftMotor <= 0 && pattern.RightMotor <= 0)
        {
            return;
        }

        var gamepad = GamepadRegistry.Find(deviceId) ?? GamepadRegistry.FindAny();
        if (gamepad is null)
        {
            await PlayXInputAsync(deviceId, pattern, cancellationToken);
            return;
        }

        try
        {
            gamepad.Vibration = new GamepadVibration
            {
                LeftMotor = Clamp(pattern.LeftMotor),
                RightMotor = Clamp(pattern.RightMotor)
            };
        }
        catch
        {
            return;
        }

        await Task.Delay(pattern.Duration, cancellationToken);

        try
        {
            gamepad.Vibration = new GamepadVibration();
        }
        catch
        {
            // 手柄中途断开时停止震动失败可忽略。
        }
    }

    private static async ValueTask PlayXInputAsync(
        string deviceId,
        HapticPattern pattern,
        CancellationToken cancellationToken)
    {
        if (!XInputNative.TryGetDeviceIndex(deviceId, out var index)
            || !XInputNative.TrySetVibration(
                index,
                ToMotorSpeed(pattern.LeftMotor),
                ToMotorSpeed(pattern.RightMotor)))
        {
            return;
        }

        await Task.Delay(pattern.Duration, cancellationToken);
        _ = XInputNative.TrySetVibration(index, 0, 0);
    }

    private static ushort ToMotorSpeed(double value) =>
        (ushort)Math.Round(Clamp(value) * ushort.MaxValue);

    private static double Clamp(double value) => Math.Clamp(value, 0.0, 1.0);
}
