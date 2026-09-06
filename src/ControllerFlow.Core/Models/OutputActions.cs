using System.Text.Json.Serialization;

namespace ControllerFlow.Core.Models;

public enum MouseOperation
{
    None = 0,
    Move,
    LeftClick,
    RightClick,
    MiddleClick,
    ScrollVertical,
    ScrollHorizontal
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyboardShortcutAction), "keyboardShortcut")]
[JsonDerivedType(typeof(MouseAction), "mouse")]
[JsonDerivedType(typeof(MediaKeyAction), "mediaKey")]
[JsonDerivedType(typeof(LaunchApplicationAction), "launchApplication")]
public abstract record OutputAction;

/// <summary>
/// 键盘组合键动作。
/// KeyDownOnly=true 时只按下不释放（由引擎在释放事件时配对抬起），
/// KeyUpOnly=true 时只释放不按下。两者同时为 false 表示完整的按下+释放（点按）。
/// </summary>
public sealed record KeyboardShortcutAction(
    IReadOnlyList<string> Keys,
    bool KeyDownOnly = false,
    bool KeyUpOnly = false) : OutputAction;

/// <summary>
/// 鼠标动作。
/// Move：相对移动（Amount 为水平位移，按住时逐次累加）；
/// ScrollVertical / ScrollHorizontal：滚轮（Amount 为滚动量，正值向上/向右）。
/// </summary>
public sealed record MouseAction(
    MouseOperation Operation,
    int Amount = 0) : OutputAction;

/// <summary>媒体 / 浏览器按键动作（点按一次）。</summary>
public sealed record MediaKeyAction(
    KeyCode Key) : OutputAction;

public sealed record LaunchApplicationAction(
    string ExecutablePath,
    string? Arguments = null) : OutputAction;
