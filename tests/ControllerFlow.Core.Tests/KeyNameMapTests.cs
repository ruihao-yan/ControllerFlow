using ControllerFlow.Core.Input;
using ControllerFlow.Core.Models;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class KeyNameMapTests
{
    [Theory]
    [InlineData("Ctrl", KeyCode.Control)]
    [InlineData("ctrl", KeyCode.Control)]
    [InlineData("CONTROL", KeyCode.Control)]
    [InlineData("Esc", KeyCode.Escape)]
    [InlineData("PgDn", KeyCode.PageDown)]
    [InlineData("F12", KeyCode.F12)]
    [InlineData("Num3", KeyCode.NumPad3)]
    [InlineData("NumPad3", KeyCode.NumPad3)]
    [InlineData("VolumeUp", KeyCode.VolumeUp)]
    [InlineData("Mute", KeyCode.VolumeMute)]
    [InlineData(";", KeyCode.OemSemicolon)]
    [InlineData("=", KeyCode.OemPlus)]
    [InlineData("Space", KeyCode.Space)]
    [InlineData("Win", KeyCode.LeftWindows)]
    public void TryGet_KnownNames(string name, KeyCode expected)
    {
        Assert.True(KeyNameMap.TryGet(name, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotAKey")]
    [InlineData("Ctrl+Alt")]
    public void TryGet_UnknownOrBlank_ReturnsFalse(string? name)
    {
        Assert.False(KeyNameMap.TryGet(name, out var code));
        Assert.Equal(KeyCode.None, code);
    }

    [Fact]
    public void TryGet_LetterAndDigitRange_AllResolve()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            Assert.True(KeyNameMap.TryGet(c.ToString(), out var code), $"字母 {c} 应可解析");
            Assert.Equal((KeyCode)(KeyCode.A + (c - 'A')), code);
        }

        for (var i = 0; i <= 9; i++)
        {
            Assert.True(KeyNameMap.TryGet(i.ToString(), out var code), $"数字 {i} 应可解析");
            Assert.Equal((KeyCode)(KeyCode.D0 + i), code);
        }
    }

    [Theory]
    [InlineData(KeyCode.Control, "Ctrl")]
    [InlineData(KeyCode.A, "A")]
    [InlineData(KeyCode.F12, "F12")]
    [InlineData(KeyCode.OemPlus, "=")]
    [InlineData(KeyCode.VolumeUp, "VolumeUp")]
    [InlineData(KeyCode.NumPad3, "Num3")]
    public void GetDisplayName_RoundTrips(KeyCode code, string displayName)
    {
        Assert.Equal(displayName, KeyNameMap.GetDisplayName(code));
        Assert.True(KeyNameMap.TryGet(displayName, out var parsed));
        Assert.Equal(code, parsed);
    }

    [Fact]
    public void GetDisplayName_UnknownCode_FallsBackToEnumName()
    {
        Assert.Equal("None", KeyNameMap.GetDisplayName(KeyCode.None));
    }
}