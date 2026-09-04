using ControllerFlow.Core.Models;
using ControllerFlow.Core.Profiles;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class ProfileValidatorTests
{
    private readonly ProfileValidator _validator = new();

    [Fact]
    public void Validate_ValidProfiles_NoIssues()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(),
            TestProfiles.AppProfile("游戏", new AppMatchRule(ProcessName: "game.exe"))
        };

        var issues = _validator.Validate(profiles);

        Assert.Empty(issues);
        Assert.False(ProfileValidator.HasErrors(issues));
    }

    [Fact]
    public void Validate_DuplicateProfileIds_Error()
    {
        var id = Guid.NewGuid();
        var profiles = new[]
        {
            TestProfiles.DefaultProfile("A") with { Id = id },
            TestProfiles.DefaultProfile("B") with { Id = id }
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Severity == ProfileValidationSeverity.Error
            && i.Message.Contains("Id 重复", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EmptyName_Error()
    {
        var issues = _validator.Validate([TestProfiles.DefaultProfile("   ") with { IsDefault = false, AppRules = [new AppMatchRule(ProcessName: "x")] }]);

        Assert.Contains(issues, i => i.Message.Contains("名称不能为空", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NonDefaultProfileWithoutRules_Error()
    {
        var issues = _validator.Validate([TestProfiles.DefaultProfile() with { IsDefault = false }]);

        Assert.Contains(issues, i => i.Message.Contains("没有可用于匹配目标应用的规则", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EmptyRule_Error()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile() with
            {
                IsDefault = false,
                AppRules = [new AppMatchRule(ProcessName: "x"), new AppMatchRule()]
            }
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Message.Contains("空的目标应用规则", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DuplicateBindingIds_Error()
    {
        var bindingId = Guid.NewGuid();
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(
                TestProfiles.Binding("A") with { Id = bindingId },
                TestProfiles.Binding("B") with { Id = bindingId })
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.BindingId == bindingId
            && i.Message.Contains("Binding Id 重复", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EmptyTriggerControl_Error()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("  "))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Message.Contains("ControlId 不能为空", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_KeyboardActionWithoutKeys_Error()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A", action: new KeyboardShortcutAction([])))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Message.Contains("没有配置按键", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_UnknownKeyName_Error()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A", action: new KeyboardShortcutAction(["Ctrl", "NopeKey"])))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Message.Contains("无法识别的按键名「NopeKey」", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_KeyDownAndUpOnlyTogether_Error()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A",
                action: new KeyboardShortcutAction(["Ctrl"], KeyDownOnly: true, KeyUpOnly: true)))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Message.Contains("不能同时开启", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_HeldWithKeyDownOnly_Warning()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A",
                gesture: InputGesture.Held,
                holdMilliseconds: 50,
                action: new KeyboardShortcutAction(["Ctrl"], KeyDownOnly: true)))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Severity == ProfileValidationSeverity.Warning
            && i.Message.Contains("长按触发不重复执行", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MouseNone_Error()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A",
                action: new MouseAction(MouseOperation.None)))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Message.Contains("鼠标动作类型无效", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_LaunchWithoutPath_Error()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A",
                action: new LaunchApplicationAction("  ")))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Message.Contains("没有配置程序路径", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MediaKeyNone_Error()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A",
                action: new MediaKeyAction(KeyCode.None)))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Message.Contains("媒体键动作无效", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SpeechHotkeyMode_Valid()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A",
                action: new SpeechToolAction(
                    new KeyboardShortcutAction(["Ctrl", "Space"], KeyDownOnly: true),
                    new KeyboardShortcutAction(["Ctrl", "Space"]))))
        };

        Assert.Empty(_validator.Validate(profiles));
    }

    [Fact]
    public void Validate_SpeechStopWithoutKeys_Error()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A",
                action: new SpeechToolAction(
                    new KeyboardShortcutAction(["Ctrl", "Space"]),
                    new KeyboardShortcutAction([]))))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Message.Contains("「停止」快捷键没有配置按键", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SpeechInvalidKeyName_Error()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A",
                action: new SpeechToolAction(
                    new KeyboardShortcutAction(["BadKey"]),
                    new KeyboardShortcutAction(["Space"]))))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Message.Contains("「开始」快捷键包含无法识别的按键名", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SpeechProcessMode_ValidWithoutShortcuts()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A",
                action: new SpeechToolAction(
                    new KeyboardShortcutAction([]),
                    new KeyboardShortcutAction([]),
                    ExecutablePath: @"C:\tools\stt.exe")))
        };

        Assert.Empty(_validator.Validate(profiles));
    }

    [Fact]
    public void Validate_SpeechProcessModeWithShortcuts_Warning()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile(TestProfiles.Binding("A",
                action: new SpeechToolAction(
                    new KeyboardShortcutAction(["Ctrl", "Space"]),
                    new KeyboardShortcutAction(["Space"]),
                    ExecutablePath: @"C:\tools\stt.exe")))
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Severity == ProfileValidationSeverity.Warning
            && i.Message.Contains("将使用工具路径", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MultipleDefaults_Warning()
    {
        var profiles = new[]
        {
            TestProfiles.DefaultProfile("默认A"),
            TestProfiles.DefaultProfile("默认B")
        };

        var issues = _validator.Validate(profiles);

        Assert.Contains(issues, i => i.Severity == ProfileValidationSeverity.Warning
            && i.Message.Contains("2 个默认 Profile", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!));
    }
}