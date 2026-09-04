using Windows.Gaming.Input;

namespace ControllerFlow.Windows.Input;

/// <summary>
/// 手柄实例注册表：按稳定 ID 索引 <see cref="Gamepad"/> 实例，
/// 供震动反馈层找到发出事件的那只手柄。注册表内容由
/// <see cref="WindowsGamepadSource"/> 在轮询时维护（天然支持热插拔）。
/// </summary>
public static class GamepadRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Gamepad> Gamepads = new(StringComparer.Ordinal);

    public static void Register(string deviceId, Gamepad gamepad)
    {
        lock (Sync)
        {
            Gamepads[deviceId] = gamepad;
        }
    }

    public static void Unregister(string deviceId)
    {
        lock (Sync)
        {
            Gamepads.Remove(deviceId);
        }
    }

    public static Gamepad? Find(string deviceId)
    {
        lock (Sync)
        {
            return Gamepads.TryGetValue(deviceId, out var gamepad) ? gamepad : null;
        }
    }

    /// <summary>兜底：返回任一已连接手柄（事件来源手柄已断开时使用）。</summary>
    public static Gamepad? FindAny()
    {
        lock (Sync)
        {
            return Gamepads.Values.FirstOrDefault();
        }
    }
}
