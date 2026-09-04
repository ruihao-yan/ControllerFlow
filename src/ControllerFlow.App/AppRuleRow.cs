namespace ControllerFlow.App;

/// <summary>
/// 目标应用规则的编辑行（可变对象，供 WPF 双向绑定；保存时转换为不可变
/// <see cref="ControllerFlow.Core.Models.AppMatchRule"/>）。
/// </summary>
public sealed class AppRuleRow
{
    public string ProcessName { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public string WindowTitlePattern { get; set; } = string.Empty;
}