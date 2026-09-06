using ControllerFlow.Core.Models;
using ControllerFlow.Core.Routing;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class ProfileResolverTests
{
    [Fact]
    public async Task ResolveAsync_PrefersMatchingAppProfile()
    {
        var resolver = new ProfileResolver(new StubProfileRepository(
        [
            TestProfiles.DefaultProfile(),
            TestProfiles.AppProfile("Chrome-游戏", new AppMatchRule(ProcessName: "chrome"))
        ]));

        var result = await resolver.ResolveAsync(new ForegroundApp(1, "chrome", null, "YouTube"));

        Assert.NotNull(result.Profile);
        Assert.Equal("Chrome-游戏", result.Profile!.Name);
        Assert.False(result.UsedDefaultFallback);
    }

    [Fact]
    public async Task ResolveAsync_VibeCodingProfileMatchesChatGptAfterPackageUpdate()
    {
        var vibeCoding = TestProfiles.AppProfile(
            "vibe coding",
            new AppMatchRule(
                ProcessName: "ChatGPT",
                ExecutablePath: @"G:\WindowsApps\OpenAI.Codex_26.901.5003.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"));
        var resolver = new ProfileResolver(new StubProfileRepository([vibeCoding]));
        var currentChatGpt = new ForegroundApp(
            36064,
            "ChatGPT",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.901.6511.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe",
            "ChatGPT");

        var result = await resolver.ResolveAsync(currentChatGpt);

        Assert.Equal(vibeCoding.Id, result.Profile?.Id);
        Assert.False(result.UsedDefaultFallback);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToDefaultProfile()
    {
        var resolver = new ProfileResolver(new StubProfileRepository(
        [
            TestProfiles.DefaultProfile("全局默认"),
            TestProfiles.AppProfile("Chrome-游戏", new AppMatchRule(ProcessName: "chrome"))
        ]));

        var result = await resolver.ResolveAsync(new ForegroundApp(1, "notepad", null, "笔记"));

        Assert.Equal("全局默认", result.Profile?.Name);
        Assert.True(result.UsedDefaultFallback);
    }

    [Fact]
    public async Task ResolveAsync_NoProfiles_ReturnsNull()
    {
        var resolver = new ProfileResolver(new StubProfileRepository([]));

        var result = await resolver.ResolveAsync(new ForegroundApp(1, "chrome", null, "YouTube"));

        Assert.Null(result.Profile);
        Assert.False(result.UsedDefaultFallback);
    }

    [Fact]
    public async Task ResolveAsync_NoDefaultAndNoMatch_ReturnsNull()
    {
        var resolver = new ProfileResolver(new StubProfileRepository(
        [
            TestProfiles.AppProfile("Chrome-游戏", new AppMatchRule(ProcessName: "chrome"))
        ]));

        var result = await resolver.ResolveAsync(new ForegroundApp(9, "explorer", null, "资源管理器"));

        Assert.Null(result.Profile);
    }

    [Fact]
    public async Task ResolveAsync_MultipleAppProfiles_HighestPriorityWins()
    {
        var resolver = new ProfileResolver(new StubProfileRepository(
        [
            TestProfiles.AppProfile("低优先级", new AppMatchRule(ProcessName: "chrome"), priority: 10),
            TestProfiles.AppProfile("高优先级", new AppMatchRule(ProcessName: "chrome"), priority: 50)
        ]));

        var result = await resolver.ResolveAsync(new ForegroundApp(1, "chrome", null, "YouTube"));

        Assert.Equal("高优先级", result.Profile?.Name);
    }

    [Fact]
    public async Task ResolveAsync_MultipleDefaults_HighestPriorityWins()
    {
        var resolver = new ProfileResolver(new StubProfileRepository(
        [
            new ControllerProfile(Guid.NewGuid(), "默认A", Priority: 0, IsDefault: true, [], []),
            new ControllerProfile(Guid.NewGuid(), "默认B", Priority: 20, IsDefault: true, [], [])
        ]));

        var result = await resolver.ResolveAsync(new ForegroundApp(1, "chrome", null, "YouTube"));

        Assert.Equal("默认B", result.Profile?.Name);
        Assert.True(result.UsedDefaultFallback);
    }

    [Fact]
    public async Task ResolveAsync_DisabledProfiles_AreIgnored()
    {
        var resolver = new ProfileResolver(new StubProfileRepository(
        [
            TestProfiles.AppProfile("禁用应用配置", new AppMatchRule(ProcessName: "chrome")) with { Enabled = false },
            TestProfiles.DefaultProfile("禁用默认") with { Enabled = false }
        ]));

        var result = await resolver.ResolveAsync(new ForegroundApp(1, "chrome", null, "YouTube"));

        Assert.Null(result.Profile);
    }

    [Fact]
    public async Task ResolveAsync_NullApp_OnlyDefaultProfile()
    {
        var resolver = new ProfileResolver(new StubProfileRepository(
        [
            TestProfiles.AppProfile("应用配置", new AppMatchRule(ProcessName: "chrome")),
            TestProfiles.DefaultProfile()
        ]));

        var result = await resolver.ResolveAsync(null);

        Assert.True(result.UsedDefaultFallback);
        Assert.True(result.Profile?.IsDefault);
    }
}