using ControllerFlow.Core.Models;

namespace ControllerFlow.Core.Input;

/// <summary>
/// 用户可读的按键名（如 "Ctrl"、"A"、"F5"、"VolumeUp"、"+"）与
/// <see cref="KeyCode"/> 之间的映射。映射表不依赖 Windows API，
/// 仅共享与 Windows 虚拟键码一致的枚举值。
/// </summary>
public static class KeyNameMap
{
    private static readonly IReadOnlyDictionary<string, KeyCode> ByName = BuildNameMap();
    private static readonly IReadOnlyDictionary<KeyCode, string> DisplayNames = BuildDisplayMap();

    /// <summary>尝试将按键名解析为 <see cref="KeyCode"/>（不区分大小写）。</summary>
    public static bool TryGet(string? name, out KeyCode code)
    {
        code = KeyCode.None;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return ByName.TryGetValue(name.Trim(), out code);
    }

    /// <summary>获取按键码的用户可读显示名（如 <see cref="KeyCode.Control"/> → "Ctrl"）。</summary>
    public static string GetDisplayName(KeyCode code) =>
        DisplayNames.TryGetValue(code, out var name) ? name : code.ToString();

    private static Dictionary<string, KeyCode> BuildNameMap()
    {
        var map = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase);

        for (var c = 'A'; c <= 'Z'; c++)
        {
            map[c.ToString()] = (KeyCode)(KeyCode.A + (c - 'A'));
        }

        for (var i = 0; i <= 9; i++)
        {
            map[i.ToString()] = (KeyCode)(KeyCode.D0 + i);
        }

        for (var i = 1; i <= 24; i++)
        {
            map[$"F{i}"] = (KeyCode)(KeyCode.F1 + (i - 1));
        }

        for (var i = 0; i <= 9; i++)
        {
            map[$"NumPad{i}"] = (KeyCode)(KeyCode.NumPad0 + i);
            map[$"Num{i}"] = (KeyCode)(KeyCode.NumPad0 + i);
        }

        Add(map, KeyCode.Back, "Backspace", "Back");
        Add(map, KeyCode.Tab, "Tab");
        Add(map, KeyCode.Enter, "Enter", "Return");
        Add(map, KeyCode.Shift, "Shift");
        Add(map, KeyCode.LeftShift, "LeftShift", "LShift");
        Add(map, KeyCode.RightShift, "RightShift", "RShift");
        Add(map, KeyCode.Control, "Ctrl", "Control");
        Add(map, KeyCode.LeftControl, "LeftCtrl", "LeftControl", "LCtrl", "LControl");
        Add(map, KeyCode.RightControl, "RightCtrl", "RightControl", "RCtrl", "RControl");
        Add(map, KeyCode.Alt, "Alt");
        Add(map, KeyCode.LeftAlt, "LeftAlt", "LAlt");
        Add(map, KeyCode.RightAlt, "RightAlt", "RAlt");
        Add(map, KeyCode.LeftWindows, "Win", "Windows", "LeftWin", "LeftWindows", "LWin");
        Add(map, KeyCode.RightWindows, "RightWin", "RightWindows", "RWin");
        Add(map, KeyCode.Pause, "Pause");
        Add(map, KeyCode.CapsLock, "CapsLock");
        Add(map, KeyCode.Escape, "Esc", "Escape");
        Add(map, KeyCode.Space, "Space", "Spacebar");
        Add(map, KeyCode.PageUp, "PageUp", "PgUp");
        Add(map, KeyCode.PageDown, "PageDown", "PgDn");
        Add(map, KeyCode.End, "End");
        Add(map, KeyCode.Home, "Home");
        Add(map, KeyCode.Left, "Left");
        Add(map, KeyCode.Up, "Up");
        Add(map, KeyCode.Right, "Right");
        Add(map, KeyCode.Down, "Down");
        Add(map, KeyCode.PrintScreen, "PrintScreen", "PrtSc");
        Add(map, KeyCode.Insert, "Insert", "Ins");
        Add(map, KeyCode.Delete, "Delete", "Del");
        Add(map, KeyCode.NumLock, "NumLock");
        Add(map, KeyCode.ScrollLock, "ScrollLock");
        Add(map, KeyCode.Multiply, "NumMultiply", "Multiply", "Num*");
        Add(map, KeyCode.Add, "NumAdd", "Add", "Num+");
        Add(map, KeyCode.Subtract, "NumSubtract", "Subtract", "Num-");
        Add(map, KeyCode.Decimal, "NumDecimal", "Decimal", "Num.");
        Add(map, KeyCode.Divide, "NumDivide", "Divide", "Num/");

        Add(map, KeyCode.VolumeMute, "VolumeMute", "Mute");
        Add(map, KeyCode.VolumeDown, "VolumeDown", "VolDown");
        Add(map, KeyCode.VolumeUp, "VolumeUp", "VolUp");
        Add(map, KeyCode.MediaPlayPause, "MediaPlayPause", "PlayPause");
        Add(map, KeyCode.MediaNextTrack, "MediaNextTrack", "NextTrack");
        Add(map, KeyCode.MediaPreviousTrack, "MediaPreviousTrack", "PreviousTrack", "PrevTrack");
        Add(map, KeyCode.MediaStop, "MediaStop");
        Add(map, KeyCode.BrowserBack, "BrowserBack");
        Add(map, KeyCode.BrowserForward, "BrowserForward");
        Add(map, KeyCode.BrowserRefresh, "BrowserRefresh");
        Add(map, KeyCode.BrowserStop, "BrowserStop");
        Add(map, KeyCode.BrowserSearch, "BrowserSearch");
        Add(map, KeyCode.BrowserFavorites, "BrowserFavorites");
        Add(map, KeyCode.BrowserHome, "BrowserHome");

        Add(map, KeyCode.OemSemicolon, ";", "Semicolon", "OemSemicolon", "Oem1");
        Add(map, KeyCode.OemPlus, "=", "Plus", "OemPlus");
        Add(map, KeyCode.OemComma, ",", "Comma", "OemComma");
        Add(map, KeyCode.OemMinus, "-", "Minus", "OemMinus");
        Add(map, KeyCode.OemPeriod, ".", "Period", "OemPeriod");
        Add(map, KeyCode.OemQuestion, "/", "Slash", "OemQuestion", "Oem2");
        Add(map, KeyCode.OemTilde, "`", "Tilde", "OemTilde", "Oem3", "Backquote");
        Add(map, KeyCode.OemOpenBrackets, "[", "BracketLeft", "OemOpenBrackets", "Oem4");
        Add(map, KeyCode.OemPipe, "\\", "Pipe", "Backslash", "OemPipe", "Oem5");
        Add(map, KeyCode.OemCloseBrackets, "]", "BracketRight", "OemCloseBrackets", "Oem6");
        Add(map, KeyCode.OemQuotes, "'", "Quote", "OemQuotes", "Oem7");

        return map;
    }

    private static Dictionary<KeyCode, string> BuildDisplayMap()
    {
        var map = new Dictionary<KeyCode, string>();

        for (var c = 'A'; c <= 'Z'; c++)
        {
            map[(KeyCode)(KeyCode.A + (c - 'A'))] = c.ToString();
        }

        for (var i = 0; i <= 9; i++)
        {
            map[(KeyCode)(KeyCode.D0 + i)] = i.ToString();
            map[(KeyCode)(KeyCode.NumPad0 + i)] = $"Num{i}";
        }

        for (var i = 1; i <= 24; i++)
        {
            map[(KeyCode)(KeyCode.F1 + (i - 1))] = $"F{i}";
        }

        map[KeyCode.Back] = "Backspace";
        map[KeyCode.Tab] = "Tab";
        map[KeyCode.Enter] = "Enter";
        map[KeyCode.Shift] = "Shift";
        map[KeyCode.LeftShift] = "LeftShift";
        map[KeyCode.RightShift] = "RightShift";
        map[KeyCode.Control] = "Ctrl";
        map[KeyCode.LeftControl] = "LeftCtrl";
        map[KeyCode.RightControl] = "RightCtrl";
        map[KeyCode.Alt] = "Alt";
        map[KeyCode.LeftAlt] = "LeftAlt";
        map[KeyCode.RightAlt] = "RightAlt";
        map[KeyCode.LeftWindows] = "Win";
        map[KeyCode.RightWindows] = "RightWin";
        map[KeyCode.Pause] = "Pause";
        map[KeyCode.CapsLock] = "CapsLock";
        map[KeyCode.Escape] = "Esc";
        map[KeyCode.Space] = "Space";
        map[KeyCode.PageUp] = "PageUp";
        map[KeyCode.PageDown] = "PageDown";
        map[KeyCode.End] = "End";
        map[KeyCode.Home] = "Home";
        map[KeyCode.Left] = "Left";
        map[KeyCode.Up] = "Up";
        map[KeyCode.Right] = "Right";
        map[KeyCode.Down] = "Down";
        map[KeyCode.PrintScreen] = "PrintScreen";
        map[KeyCode.Insert] = "Insert";
        map[KeyCode.Delete] = "Delete";
        map[KeyCode.NumLock] = "NumLock";
        map[KeyCode.ScrollLock] = "ScrollLock";
        map[KeyCode.Multiply] = "Num*";
        map[KeyCode.Add] = "Num+";
        map[KeyCode.Subtract] = "Num-";
        map[KeyCode.Decimal] = "Num.";
        map[KeyCode.Divide] = "Num/";
        map[KeyCode.VolumeMute] = "VolumeMute";
        map[KeyCode.VolumeDown] = "VolumeDown";
        map[KeyCode.VolumeUp] = "VolumeUp";
        map[KeyCode.MediaPlayPause] = "MediaPlayPause";
        map[KeyCode.MediaNextTrack] = "MediaNextTrack";
        map[KeyCode.MediaPreviousTrack] = "MediaPreviousTrack";
        map[KeyCode.MediaStop] = "MediaStop";
        map[KeyCode.BrowserBack] = "BrowserBack";
        map[KeyCode.BrowserForward] = "BrowserForward";
        map[KeyCode.BrowserRefresh] = "BrowserRefresh";
        map[KeyCode.BrowserStop] = "BrowserStop";
        map[KeyCode.BrowserSearch] = "BrowserSearch";
        map[KeyCode.BrowserFavorites] = "BrowserFavorites";
        map[KeyCode.BrowserHome] = "BrowserHome";
        map[KeyCode.OemSemicolon] = ";";
        map[KeyCode.OemPlus] = "=";
        map[KeyCode.OemComma] = ",";
        map[KeyCode.OemMinus] = "-";
        map[KeyCode.OemPeriod] = ".";
        map[KeyCode.OemQuestion] = "/";
        map[KeyCode.OemTilde] = "`";
        map[KeyCode.OemOpenBrackets] = "[";
        map[KeyCode.OemPipe] = "\\";
        map[KeyCode.OemCloseBrackets] = "]";
        map[KeyCode.OemQuotes] = "'";

        return map;
    }

    private static void Add(IDictionary<string, KeyCode> map, KeyCode code, params string[] names)
    {
        foreach (var name in names)
        {
            map[name] = code;
        }
    }
}
