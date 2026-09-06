using ControllerFlow.Core.Models;
using ControllerFlow.Core.Routing;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class AppRuleMatcherTests
{
    private static readonly ForegroundApp Code = new(1, "Code", @"C:\Program Files\VS Code\Code.exe", "Program.cs — Visual Studio Code");

    [Fact]
    public void Matches_ProcessName_IgnoreCase()
    {
        var rule = new AppMatchRule(ProcessName: "code");

        Assert.True(AppRuleMatcher.Matches(rule, Code));
        Assert.False(AppRuleMatcher.Matches(rule, new ForegroundApp(2, "chrome", null, "新标签页")));
    }

    [Fact]
    public void Matches_ExecutablePath_IgnoreCase()
    {
        var rule = new AppMatchRule(ExecutablePath: @"c:\program files\vs code\code.exe");

        Assert.True(AppRuleMatcher.Matches(rule, Code));
    }

    [Fact]
    public void Matches_WindowsPackagePath_IgnoresInstallRootAndPackageVersion()
    {
        var rule = new AppMatchRule(
            ProcessName: "ChatGPT",
            ExecutablePath: @"G:\WindowsApps\OpenAI.Codex_26.901.5003.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe");
        var currentChatGpt = new ForegroundApp(
            36064,
            "ChatGPT",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.901.6511.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe",
            "ChatGPT");

        Assert.True(AppRuleMatcher.Matches(rule, currentChatGpt));
    }

    [Fact]
    public void Matches_WindowsPackagePath_RejectsDifferentPackage()
    {
        var rule = new AppMatchRule(
            ProcessName: "ChatGPT",
            ExecutablePath: @"G:\WindowsApps\OpenAI.Codex_26.901.5003.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe");
        var otherPackage = new ForegroundApp(
            1,
            "ChatGPT",
            @"C:\Program Files\WindowsApps\Other.Codex_26.901.6511.0_x64__otherpublisher\app\ChatGPT.exe",
            "ChatGPT");

        Assert.False(AppRuleMatcher.Matches(rule, otherPackage));
    }

    [Fact]
    public void Matches_WindowTitle_RegexIgnoreCase()
    {
        var rule = new AppMatchRule(WindowTitlePattern: @"program\.cs.*visual studio");

        Assert.True(AppRuleMatcher.Matches(rule, Code));
    }

    [Fact]
    public void Matches_AllConditionsMustHold()
    {
        var rule = new AppMatchRule(
            ProcessName: "Code",
            ExecutablePath: @"C:\Program Files\VS Code\Code.exe",
            WindowTitlePattern: @"program\.cs");

        Assert.True(AppRuleMatcher.Matches(rule, Code));

        var otherTitle = Code with { WindowTitle = "README.md" };
        Assert.False(AppRuleMatcher.Matches(rule, otherTitle));
    }

    [Fact]
    public void Matches_InvalidRegex_ReturnsFalse()
    {
        var rule = new AppMatchRule(WindowTitlePattern: @"[不合法");

        Assert.False(AppRuleMatcher.Matches(rule, Code));
    }

    [Fact]
    public void Matches_CatastrophicRegex_ReturnsFalseWithoutBlocking()
    {
        var rule = new AppMatchRule(WindowTitlePattern: @"^(a+)+$");
        var app = new ForegroundApp(
            3,
            "editor",
            null,
            new string('a', 40) + "b" + new string('a', 10));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = AppRuleMatcher.Matches(rule, app);
        sw.Stop();

        Assert.False(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"正则超时未生效：耗时 {sw.Elapsed}");
    }

    [Fact]
    public void Matches_EmptyRule_ReturnsFalseAndHasNoCondition()
    {
        var rule = new AppMatchRule();

        Assert.False(AppRuleMatcher.HasCondition(rule));
        Assert.False(AppRuleMatcher.Matches(rule, Code));
    }

    [Fact]
    public void Matches_Profile_AnyRuleSatisfies()
    {
        var profile = TestProfiles.AppProfile(
            "编辑器",
            new AppMatchRule(ProcessName: "notepad"));

        Assert.True(AppRuleMatcher.Matches(profile, new ForegroundApp(5, "notepad", null, "新建文本文档")));
        Assert.False(AppRuleMatcher.Matches(profile, Code));
    }

    [Fact]
    public void Matches_NullTitlePattern_IgnoresTitle()
    {
        var rule = new AppMatchRule(ProcessName: "Code", WindowTitlePattern: null);

        Assert.True(AppRuleMatcher.Matches(rule, new ForegroundApp(1, "Code", null, string.Empty)));
    }
}