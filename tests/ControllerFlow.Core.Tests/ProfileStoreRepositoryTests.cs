using ControllerFlow.Core.Models;
using ControllerFlow.Core.Profiles;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class ProfileStoreRepositoryTests
{
    [Fact]
    public async Task GetEnabledAsync_FiltersDisabledAndLoadsLazily()
    {
        var store = new InMemoryProfileStore
        (
            seed:
            [
                TestProfiles.DefaultProfile("启用"),
                TestProfiles.DefaultProfile("禁用") with { Enabled = false }
            ]
        );

        var repository = new ProfileStoreRepository(store);
        var result = await repository.GetEnabledAsync();

        var profile = Assert.Single(result);
        Assert.Equal("启用", profile.Name);
    }

    [Fact]
    public async Task GetEnabledAsync_MissingFile_ReturnsEmpty()
    {
        var store = new InMemoryProfileStore { FileExists = false };
        var repository = new ProfileStoreRepository(store);

        Assert.Empty(await repository.GetEnabledAsync());
    }

    [Fact]
    public async Task GetAllAsync_IncludesDisabled()
    {
        var store = new InMemoryProfileStore(
            seed:
            [
                TestProfiles.DefaultProfile("启用"),
                TestProfiles.DefaultProfile("禁用") with { Enabled = false }
            ]);
        var repository = new ProfileStoreRepository(store);

        var result = await repository.GetAllAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReloadAsync_ReflectsStoreChanges()
    {
        var store = new InMemoryProfileStore(seed: [TestProfiles.DefaultProfile("旧")]);
        var repository = new ProfileStoreRepository(store);
        _ = await repository.GetAllAsync();

        store.Current = [TestProfiles.AppProfile("新", new AppMatchRule(ProcessName: "x"))];
        await repository.ReloadAsync();

        var result = await repository.GetAllAsync();
        Assert.Equal("新", Assert.Single(result).Name);
    }

    [Fact]
    public async Task GetEnabledAsync_StoreLoadErrors_Propagate()
    {
        var store = new InMemoryProfileStore(seedIssues: [new ProfileValidationIssue(ProfileValidationSeverity.Error, "坏")]);
        var repository = new ProfileStoreRepository(store);

        await Assert.ThrowsAsync<ProfileStoreException>(() => repository.GetEnabledAsync().AsTask());
    }
}