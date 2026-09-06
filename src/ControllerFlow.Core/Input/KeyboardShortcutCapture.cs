using ControllerFlow.Core.Models;

namespace ControllerFlow.Core.Input;

/// <summary>
/// 将实际按下的虚拟键码整理为稳定的快捷键显示顺序。
/// 修饰键统一为 Ctrl、Alt、Shift、Win，避免左右侧按键造成重复配置。
/// </summary>
public static class KeyboardShortcutCapture
{
    public static IReadOnlyList<string> Format(IEnumerable<KeyCode> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        return keys
            .Where(key => key != KeyCode.None)
            .Select(NormalizeModifier)
            .Distinct()
            .OrderBy(GetOrder)
            .ThenBy(key => (int)key)
            .Select(KeyNameMap.GetDisplayName)
            .ToArray();
    }

    public static bool IsModifier(KeyCode key) => key switch
    {
        KeyCode.Shift or KeyCode.LeftShift or KeyCode.RightShift
            or KeyCode.Control or KeyCode.LeftControl or KeyCode.RightControl
            or KeyCode.Alt or KeyCode.LeftAlt or KeyCode.RightAlt
            or KeyCode.LeftWindows or KeyCode.RightWindows => true,
        _ => false
    };

    public static KeyCode NormalizeModifier(KeyCode key) => key switch
    {
        KeyCode.LeftShift or KeyCode.RightShift => KeyCode.Shift,
        KeyCode.LeftControl or KeyCode.RightControl => KeyCode.Control,
        KeyCode.LeftAlt or KeyCode.RightAlt => KeyCode.Alt,
        KeyCode.RightWindows => KeyCode.LeftWindows,
        _ => key
    };

    private static int GetOrder(KeyCode key) => key switch
    {
        KeyCode.Control => 0,
        KeyCode.Alt => 1,
        KeyCode.Shift => 2,
        KeyCode.LeftWindows => 3,
        _ => 4
    };
}
