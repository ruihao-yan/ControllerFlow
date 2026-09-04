using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;

namespace ControllerFlow.Core.Routing;

/// <summary>为指定前台应用解析出的 Profile 结果。</summary>
/// <param name="Profile">命中的 Profile；无任何命中时为 null。</param>
/// <param name="UsedDefaultFallback">是否因应用 Profile 未命中而回退到默认 Profile。</param>
public sealed record ProfileResolution(
    ControllerProfile? Profile,
    bool UsedDefaultFallback = false);

/// <summary>
/// 按“目标应用匹配优先、默认 Profile 兜底”的策略解析当前 Profile。
/// 供 <see cref="ProfileRouter"/> 与前台应用监控 / UI 复用。
/// </summary>
public sealed class ProfileResolver(IProfileRepository profileRepository)
{
    public async ValueTask<ProfileResolution> ResolveAsync(
        ForegroundApp? app,
        CancellationToken cancellationToken = default)
    {
        var profiles = await profileRepository.GetEnabledAsync(cancellationToken);

        var appProfile = profiles
            .Where(profile => profile.Enabled
                && !profile.IsDefault
                && app is not null
                && AppRuleMatcher.Matches(profile, app))
            .OrderByDescending(profile => profile.Priority)
            .FirstOrDefault();

        if (appProfile is not null)
        {
            return new ProfileResolution(appProfile);
        }

        var defaultProfile = profiles
            .Where(profile => profile.Enabled && profile.IsDefault)
            .OrderByDescending(profile => profile.Priority)
            .FirstOrDefault();

        return defaultProfile is null
            ? new ProfileResolution(null)
            : new ProfileResolution(defaultProfile, UsedDefaultFallback: true);
    }
}
