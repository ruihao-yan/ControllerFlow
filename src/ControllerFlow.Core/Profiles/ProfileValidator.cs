using ControllerFlow.Core.Input;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Routing;

namespace ControllerFlow.Core.Profiles;

public enum ProfileValidationSeverity
{
    Error,
    Warning
}

/// <summary>单条校验问题。</summary>
public sealed record ProfileValidationIssue(
    ProfileValidationSeverity Severity,
    string Message,
    Guid? ProfileId = null,
    Guid? BindingId = null);

/// <summary>
/// Profile 静态校验：命名、匹配规则、绑定、动作参数、ID 唯一性。
/// Error 级别的配置不允许写入磁盘；Warning 仅提示。
/// </summary>
public sealed class ProfileValidator
{
    public IReadOnlyList<ProfileValidationIssue> Validate(IReadOnlyList<ControllerProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var issues = new List<ProfileValidationIssue>();
        var profileIds = new HashSet<Guid>();
        var defaultCount = 0;

        foreach (var profile in profiles)
        {
            if (!profileIds.Add(profile.Id))
            {
                issues.Add(Error($"Profile Id 重复：{profile.Id}", profile.Id));
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                issues.Add(Error("Profile 名称不能为空。", profile.Id));
            }

            if (profile.IsDefault)
            {
                defaultCount++;
            }

            if (!profile.IsDefault && !profile.AppRules.Any(AppRuleMatcher.HasCondition))
            {
                issues.Add(Error(
                    $"Profile「{profile.Name}」没有可用于匹配目标应用的规则（进程名 / 路径 / 窗口标题正则至少填写一项）。",
                    profile.Id));
            }

            foreach (var rule in profile.AppRules.Where(rule => !AppRuleMatcher.HasCondition(rule)))
            {
                issues.Add(Error(
                    $"Profile「{profile.Name}」包含空的目标应用规则（进程名 / 路径 / 窗口标题正则至少填写一项）。",
                    profile.Id));
            }

            var bindingIds = new HashSet<Guid>();
            foreach (var binding in profile.Bindings)
            {
                if (!bindingIds.Add(binding.Id))
                {
                    issues.Add(Error($"Binding Id 重复：{binding.Id}", profile.Id, binding.Id));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(binding.Trigger.ControlId))
                {
                    issues.Add(Error("触发键 ControlId 不能为空。", profile.Id, binding.Id));
                }

                foreach (var issue in ValidateAction(binding, profile))
                {
                    issues.Add(issue);
                }
            }
        }

        if (defaultCount > 1)
        {
            issues.Add(Warning(
                $"存在 {defaultCount} 个默认 Profile，路由时只会使用优先级最高的一个。"));
        }

        return issues;
    }

    public static bool HasErrors(IReadOnlyList<ProfileValidationIssue> issues) =>
        issues.Any(issue => issue.Severity == ProfileValidationSeverity.Error);

    private static IEnumerable<ProfileValidationIssue> ValidateAction(
        InputBinding binding,
        ControllerProfile profile)
    {
        switch (binding.Action)
        {
            case KeyboardShortcutAction keyboard when keyboard.Keys.Count == 0:
                yield return Error("键盘快捷键动作没有配置按键。", profile.Id, binding.Id);
                yield break;

            case KeyboardShortcutAction keyboard:
                foreach (var key in keyboard.Keys.Where(key => !KeyNameMap.TryGet(key, out _)))
                {
                    yield return Error($"无法识别的按键名「{key}」。", profile.Id, binding.Id);
                }

                if (keyboard.KeyDownOnly && keyboard.KeyUpOnly)
                {
                    yield return Error("KeyDownOnly 与 KeyUpOnly 不能同时开启。", profile.Id, binding.Id);
                }

                if (binding.Trigger.Gesture == InputGesture.Held
                    && (keyboard.KeyDownOnly || keyboard.KeyUpOnly))
                {
                    yield return Warning(
                        "长按触发不重复执行 KeyDownOnly / KeyUpOnly 动作。",
                        profile.Id,
                        binding.Id);
                }

                yield break;

            case MouseAction { Operation: MouseOperation.None }:
                yield return Error("鼠标动作类型无效。", profile.Id, binding.Id);
                yield break;

            case LaunchApplicationAction launch when string.IsNullOrWhiteSpace(launch.ExecutablePath):
                yield return Error("启动程序动作没有配置程序路径。", profile.Id, binding.Id);
                yield break;

            case MediaKeyAction { Key: KeyCode.None }:
                yield return Error("媒体键动作无效。", profile.Id, binding.Id);
                yield break;

            case SpeechToolAction speech:
                if (string.IsNullOrWhiteSpace(speech.ExecutablePath))
                {
                    // 快捷键模式：开始 / 停止快捷键均必须配置按键。
                    foreach (var (shortcut, label) in new[]
                    {
                        (speech.Start, "开始"),
                        (speech.Stop, "停止")
                    })
                    {
                        if (shortcut.Keys.Count == 0)
                        {
                            yield return Error($"语音动作的「{label}」快捷键没有配置按键。", profile.Id, binding.Id);
                            continue;
                        }

                        foreach (var key in shortcut.Keys.Where(key => !KeyNameMap.TryGet(key, out _)))
                        {
                            yield return Error(
                                $"语音动作的「{label}」快捷键包含无法识别的按键名「{key}」。",
                                profile.Id,
                                binding.Id);
                        }

                        if (shortcut.KeyDownOnly && shortcut.KeyUpOnly)
                        {
                            yield return Error(
                                $"语音动作的「{label}」快捷键 KeyDownOnly 与 KeyUpOnly 不能同时开启。",
                                profile.Id,
                                binding.Id);
                        }
                    }
                }
                else if (speech.Start.Keys.Count > 0 || speech.Stop.Keys.Count > 0)
                {
                    yield return Warning(
                        "语音动作同时配置了工具路径与快捷键，将使用工具路径（进程模式），快捷键被忽略。",
                        profile.Id,
                        binding.Id);
                }

                yield break;
        }
    }

    private static ProfileValidationIssue Error(
        string message,
        Guid? profileId = null,
        Guid? bindingId = null) =>
        new(ProfileValidationSeverity.Error, message, profileId, bindingId);

    private static ProfileValidationIssue Warning(
        string message,
        Guid? profileId = null,
        Guid? bindingId = null) =>
        new(ProfileValidationSeverity.Warning, message, profileId, bindingId);
}
