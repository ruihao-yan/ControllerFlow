using ControllerFlow.Core.Input;
using ControllerFlow.Core.Models;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class KeyboardShortcutCaptureTests
{
    [Fact]
    public void Format_OrdersAndNormalizesModifiers()
    {
        var names = KeyboardShortcutCapture.Format([
            KeyCode.RightShift,
            KeyCode.C,
            KeyCode.LeftControl,
            KeyCode.RightControl,
            KeyCode.LeftAlt,
            KeyCode.RightWindows
        ]);

        Assert.Equal(["Ctrl", "Alt", "Shift", "Win", "C"], names);
    }

    [Fact]
    public void Format_EmptyKeys_ReturnsEmpty()
    {
        Assert.Empty(KeyboardShortcutCapture.Format([]));
    }

    [Theory]
    [InlineData(KeyCode.Control)]
    [InlineData(KeyCode.LeftControl)]
    [InlineData(KeyCode.RightControl)]
    [InlineData(KeyCode.Alt)]
    [InlineData(KeyCode.Shift)]
    [InlineData(KeyCode.LeftWindows)]
    public void IsModifier_RecognizesModifierKeys(KeyCode key)
    {
        Assert.True(KeyboardShortcutCapture.IsModifier(key));
    }

    [Fact]
    public void IsModifier_RejectsMainKey()
    {
        Assert.False(KeyboardShortcutCapture.IsModifier(KeyCode.C));
    }
}
