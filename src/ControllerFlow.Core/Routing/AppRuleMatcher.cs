using System.Text.RegularExpressions;
using ControllerFlow.Core.Models;

namespace ControllerFlow.Core.Routing;

/// <summary>
/// Profile 目标应用匹配规则求值：进程名（忽略大小写）、可执行路径、
/// 窗口标题正则（带超时，非法正则视为不匹配）。Windows 应用包路径允许安装位置与版本变化。
/// 不依赖 Windows API。
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
            || ExecutablePathsMatch(rule.ExecutablePath, app.ExecutablePath);

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

    private static bool ExecutablePathsMatch(string expectedPath, string? actualPath)
    {
        if (string.IsNullOrWhiteSpace(actualPath))
        {
            return false;
        }

        if (string.Equals(expectedPath, actualPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryParseWindowsPackageExecutable(expectedPath, out var expectedPackage)
            && TryParseWindowsPackageExecutable(actualPath, out var actualPackage)
            && expectedPackage.Matches(actualPackage);
    }

    private static bool TryParseWindowsPackageExecutable(
        string path,
        out WindowsPackageExecutable executable)
    {
        executable = default;
        var normalized = path.Replace('/', '\\');
        const string marker = "\\WindowsApps\\";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var packageStart = markerIndex + marker.Length;
        var packageEnd = normalized.IndexOf('\\', packageStart);
        if (packageEnd < 0 || packageEnd == normalized.Length - 1)
        {
            return false;
        }

        var packageFolder = normalized[packageStart..packageEnd];
        var parts = packageFolder.Split('_');
        var versionIndex = Array.FindIndex(parts, 1, part => Version.TryParse(part, out _));
        if (versionIndex <= 0 || parts.Length - versionIndex < 4)
        {
            return false;
        }

        executable = new WindowsPackageExecutable(
            string.Join('_', parts[..versionIndex]),
            parts[versionIndex + 1],
            string.Join('_', parts[(versionIndex + 2)..^1]),
            parts[^1],
            normalized[(packageEnd + 1)..]);
        return true;
    }

    private readonly record struct WindowsPackageExecutable(
        string Name,
        string Architecture,
        string ResourceId,
        string PublisherId,
        string RelativeExecutablePath)
    {
        public bool Matches(WindowsPackageExecutable other) =>
            string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Architecture, other.Architecture, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ResourceId, other.ResourceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(PublisherId, other.PublisherId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(RelativeExecutablePath, other.RelativeExecutablePath, StringComparison.OrdinalIgnoreCase);
    }
}
