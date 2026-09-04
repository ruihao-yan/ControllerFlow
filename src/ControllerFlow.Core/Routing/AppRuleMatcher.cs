using System.Text.RegularExpressions;
using ControllerFlow.Core.Models;

namespace ControllerFlow.Core.Routing;

/// <summary>
/// Profile 目标应用匹配规则求值：进程名（忽略大小写）、完整路径（忽略大小写）、
/// 窗口标题正则（带超时，非法正则视为不匹配）。不依赖 Windows API。
/// </summary>
public static class AppRuleMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>规则是否包含至少一个匹配条件（进程名 / 路径 / 标题正则）。</summary>
    public static bool HasCondition(AppMatchRule rule) =>
        !string.IsNullOrWhiteSpace(rule.ProcessName)
        || !string.IsNullOrWhiteSpace(rule.ExecutablePath)
        || !string.IsNullOrWhiteSpace(rule.WindowTitlePattern);

    public static bool Matches(ControllerProfile profile, ForegroundApp app) =>
        profile.AppRules.Any(rule => Matches(rule, app));

    public static bool Matches(AppMatchRule rule, ForegroundApp app)
    {
        if (!HasCondition(rule))
        {
            return false;
        }

        var processNameMatches = string.IsNullOrWhiteSpace(rule.ProcessName)
            || string.Equals(rule.ProcessName, app.ProcessName, StringComparison.OrdinalIgnoreCase);

        var pathMatches = string.IsNullOrWhiteSpace(rule.ExecutablePath)
            || string.Equals(rule.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase);

        var titleMatches = true;
        if (!string.IsNullOrWhiteSpace(rule.WindowTitlePattern))
        {
            try
            {
                titleMatches = Regex.IsMatch(
                    app.WindowTitle,
                    rule.WindowTitlePattern,
                    RegexOptions.IgnoreCase,
                    RegexTimeout);
            }
            catch (ArgumentException)
            {
                // 非法正则按不匹配处理，避免一条错误规则拖垮整个路由。
                titleMatches = false;
            }
            catch (RegexMatchTimeoutException)
            {
                // 灾难性回溯导致超时：同样按不匹配处理，并保证路由不被阻塞。
                titleMatches = false;
            }
        }

        return processNameMatches && pathMatches && titleMatches;
    }
}
