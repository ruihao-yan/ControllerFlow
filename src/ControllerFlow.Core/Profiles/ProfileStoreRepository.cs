using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;

namespace ControllerFlow.Core.Profiles;

/// <summary>
/// <see cref="IProfileRepository"/> 的持久化实现：首次访问时从
/// <see cref="IProfileStore"/> 加载并缓存，保存后调用 <see cref="ReloadAsync"/>
/// 让编辑结果立即对路由生效。
/// </summary>
public sealed class ProfileStoreRepository : IProfileRepository
{
    private readonly IProfileStore _store;
    private readonly object _sync = new();
    private IReadOnlyList<ControllerProfile> _profiles = [];
    private bool _loaded;

    public ProfileStoreRepository(IProfileStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async ValueTask<IReadOnlyList<ControllerProfile>> GetEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        lock (_sync)
        {
            return _profiles.Where(profile => profile.Enabled).ToArray();
        }
    }

    /// <summary>返回全部 Profile（含禁用），供编辑器使用。</summary>
    public async ValueTask<IReadOnlyList<ControllerProfile>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        lock (_sync)
        {
            return _profiles;
        }
    }

    /// <summary>重新从存储加载（编辑器保存 / 导入 / 删除后调用）。</summary>
    public async ValueTask ReloadAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _store.LoadAsync(cancellationToken);
        lock (_sync)
        {
            _profiles = profiles;
            _loaded = true;
        }
    }

    private async ValueTask EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_loaded)
            {
                return;
            }
        }

        await ReloadAsync(cancellationToken);
    }
}
