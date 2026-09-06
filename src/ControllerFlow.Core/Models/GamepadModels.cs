namespace ControllerFlow.Core.Models;

/// <summary>
/// 一次手柄采样帧。由 Windows 适配层读取原始状态后转换成与平台无关的帧，
/// 交给 <see cref="Input.GamepadInputTracker"/> 归一化。
/// </summary>
/// <param name="PressedButtons">当前按下的数字按键（使用 <see cref="GamepadControls"/> 常量）。</param>
/// <param name="LeftThumbX">左摇杆 X 轴，范围 [-1, 1]，正值向右。</param>
/// <param name="LeftThumbY">左摇杆 Y 轴，范围 [-1, 1]，正值向上。</param>
/// <param name="RightThumbX">右摇杆 X 轴，范围 [-1, 1]，正值向右。</param>
/// <param name="RightThumbY">右摇杆 Y 轴，范围 [-1, 1]，正值向上。</param>
/// <param name="LeftTrigger">左扳机，范围 [0, 1]。</param>
/// <param name="RightTrigger">右扳机，范围 [0, 1]。</param>
public sealed record GamepadFrame(
    IReadOnlySet<string> PressedButtons,
    double LeftThumbX = 0,
    double LeftThumbY = 0,
    double RightThumbX = 0,
    double RightThumbY = 0,
    double LeftTrigger = 0,
    double RightTrigger = 0);

/// <summary>
/// 归一化后的手柄控件 ID。数字按键使用名称本身，摇杆方向与扳机
/// 由 <see cref="Input.GamepadInputTracker"/> 根据阈值生成。
/// </summary>
public static class GamepadControls
{
    public const string A = "A";
    public const string B = "B";
    public const string X = "X";
    public const string Y = "Y";

    public const string LeftBumper = "LB";
    public const string RightBumper = "RB";

    public const string LeftStickClick = "LS";
    public const string RightStickClick = "RS";

    public const string Menu = "Menu";
    public const string View = "View";

    public const string DPadUp = "DPadUp";
    public const string DPadDown = "DPadDown";
    public const string DPadLeft = "DPadLeft";
    public const string DPadRight = "DPadRight";

    public const string LeftStickUp = "LS_Up";
    public const string LeftStickDown = "LS_Down";
    public const string LeftStickLeft = "LS_Left";
    public const string LeftStickRight = "LS_Right";

    public const string RightStickUp = "RS_Up";
    public const string RightStickDown = "RS_Down";
    public const string RightStickLeft = "RS_Left";
    public const string RightStickRight = "RS_Right";

    public const string LeftTrigger = "LT";
    public const string RightTrigger = "RT";

    /// <summary>UI 与编辑器使用的全部可选控件 ID。</summary>
    public static readonly IReadOnlyList<string> All =
    [
        A, B, X, Y,
        LeftBumper, RightBumper,
        LeftStickClick, RightStickClick,
        Menu, View,
        DPadUp, DPadDown, DPadLeft, DPadRight,
        LeftStickUp, LeftStickDown, LeftStickLeft, LeftStickRight,
        RightStickUp, RightStickDown, RightStickLeft, RightStickRight,
        LeftTrigger, RightTrigger
    ];
}
