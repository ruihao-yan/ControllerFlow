using ControllerFlow.Core.Models;
using ControllerFlow.Core.Profiles;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class ProfileEditorServiceTests
{
    [Fact]
    public async Task SaveAsync_ValidProfiles_ReturnsSavedWithOnlyWarnings()
    {
        var store = new InMemoryProfileStore();
        var service = new ProfileEditorService(store);
        var profiles = new[]
        {
            TestProfiles.DefaultProfile("默认A"),
            TestProfiles.DefaultProfile("默认B") // 两个默认 → Warning
        };

        var result = await service.SaveAsync(profiles);

        Assert.True(result.Saved);
        Assert.Contains(result.Issues, i => i.Severity == ProfileValidationSeverity.Warning);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task SaveAsync_InvalidProfiles_NotSavedAndIssuesReturned()
    {
        var store = new InMemoryProfileStore();
        var service = new ProfileEditorService(store);
        var profiles = new[]
        {
            TestProfiles.DefaultProfile("坏配置",
                TestProfiles.Binding("A", action: new KeyboardShortcutAction([])))
        };

        var result = await service.SaveAsync(profiles);

        Assert.False(result.Saved);
        Assert.Contains(result.Issues, i => i.Severity == ProfileValidationSeverity.Error);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task SaveAsync_EmptyList_IsValid()
    {
        var store = new InMemoryProfileStore();
        var service = new ProfileEditorService(store);

        var result = await service.SaveAsync([]);

        Assert.True(result.Saved);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task LoadAsync_DelegatesToStore()
    {
        var store = new InMemoryProfileStore(seed: [TestProfiles.DefaultProfile("已有")]);
        var service = new ProfileEditorService(store);

        var profiles = await service.LoadAsync();

        Assert.Equal("已有", Assert.Single(profiles).Name);
    }

    [Fact]
    public async Task ImportExport_DelegateToStore()
    {
        var store = new InMemoryProfileStore();
        var service = new ProfileEditorService(store);
        var profiles = new[] { TestProfiles.DefaultProfile() };

        await service.ExportAsync(profiles, @"C:\backup.json");
        Assert.Equal(@"C:\backup.json", store.LastExportPath);

        await service.ImportAsync(@"C:\backup.json");
        Assert.Equal(@"C:\backup.json", store.LastImportPath);
        Assert.Equal(1, store.ImportCallCount);
    }

    [Fact]
    public void CreateProfile_HasDefaults()
    {
        var profile = ProfileEditorService.CreateProfile("我的配置");

        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal("我的配置", profile.Name);
        Assert.Equal(0, profile.Priority);
        Assert.False(profile.IsDefault);
        Assert.True(profile.Enabled);
        Assert.Empty(profile.AppRules);
        Assert.Empty(profile.Bindings);
    }

    [Fact]
    public void CreateProfile_BlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProfileEditorService.CreateProfile("   "));
    }

    [Fact]
    public void CreateBinding_HasDefaults()
    {
        var binding = ProfileEditorService.CreateBinding(GamepadControls.LeftBumper, InputGesture.Held);

        Assert.NotEqual(Guid.Empty, binding.Id);
        Assert.Equal(GamepadControls.LeftBumper, binding.Trigger.ControlId);
        Assert.Equal(InputGesture.Held, binding.Trigger.Gesture);
        Assert.Empty(Assert.IsType<KeyboardShortcutAction>(binding.Action).Keys);
        Assert.True(binding.Enabled);
    }
}