using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;

namespace ControllerFlow.Core.Routing;

/// <summary>
/// 输入事件路由器：先按当前前台应用解析 Profile（应用匹配优先、默认兜底），
/// 再在 Profile 内按 ControlId + 手势匹配 Binding。不依赖 Windows API。
/// </summary>
public sealed class ProfileRouter
{
    private readonly IForegroundAppProvider _foregroundAppProvider;
    private readonly ProfileResolver _resolver;

    public ProfileRouter(
        IForegroundAppProvider foregroundAppProvider,
        IProfileRepository profileRepository)
    {
        ArgumentNullException.ThrowIfNull(foregroundAppProvider);
        ArgumentNullException.ThrowIfNull(profileRepository);
        _foregroundAppProvider = foregroundAppProvider;
        _resolver = new ProfileResolver(profileRepository);
    }

    public async ValueTask<RoutingDecision> RouteAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var currentApp = await _foregroundAppProvider.GetCurrentAsync(cancellationToken);
        var resolution = await _resolver.ResolveAsync(currentApp, cancellationToken);

        if (resolution.Profile is null)
        {
            return new RoutingDecision(RoutingStatus.NoProfile);
        }

        var profileBindings = resolution.Profile.Bindings;
        var binding = profileBindings.FirstOrDefault(candidate =>
                candidate.Enabled
                && string.Equals(
                    candidate.Trigger.ControlId,
                    input.ControlId,
                    StringComparison.OrdinalIgnoreCase)
                && candidate.Trigger.Gesture == input.Gesture)
            // 释放配对：Released 事件没有精确手势匹配时，回退到同控件的
            // Pressed 手势 Binding，让引擎完成 KeyDownOnly 组合键的配对抬起。
            ?? (input.Gesture == InputGesture.Released
                ? profileBindings.FirstOrDefault(candidate =>
                    candidate.Enabled
                    && string.Equals(
                        candidate.Trigger.ControlId,
                        input.ControlId,
                        StringComparison.OrdinalIgnoreCase)
                    && candidate.Trigger.Gesture == InputGesture.Pressed)
                : null);

        return binding is null
            ? new RoutingDecision(RoutingStatus.NoBinding, resolution.Profile)
            : new RoutingDecision(RoutingStatus.Matched, resolution.Profile, binding);
    }
}
