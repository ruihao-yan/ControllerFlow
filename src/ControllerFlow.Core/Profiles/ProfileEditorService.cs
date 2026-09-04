using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;

namespace ControllerFlow.Core.Profiles;

/// <summary>一次保存操作的结果：校验通过（已落盘）或校验失败（未写入）。</summary>
/// <param name="Saved">是否已写入存储；false 表示存在 Error 级校验问题。</param>
/// <param name="Issues">校验问题列表（可能为空或仅含 Warning）。</param>
public sealed record ProfileSaveResult(bool Saved, IReadOnlyList<ProfileValidationIssue> Issues);

/// <summary>
/// Profile 编辑服务：为桌面 UI 提供加载 / 保存（校验失败不落盘，返回问题列表）/
/// 导入 / 导出与对象工厂。所有写入都经 <see cref="ProfileValidator"/> 把关；
/// 本服务不依赖 Windows API。
/// </summary>
public sealed class ProfileEditorService
{
    private readonly IProfileStore _store;
    private readonly ProfileValidator _validator = new();

    public ProfileEditorService(IProfileStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>加载全部 Profile（文件不存在时返回空列表）。</summary>
    public async ValueTask<IReadOnlyList<ControllerProfile>> LoadAsync(
        CancellationToken cancellationToken = default) =>
        await _store.LoadAsync(cancellationToken);

    /// <summary>
    /// 校验并保存全部 Profile。存在 Error 级问题时返回 <c>Saved = false</c> 且不写入；
    /// 仅含 Warning 或完全通过时写入并返回 <c>Saved = true</c>。
    /// </summary>
    public async ValueTask<ProfileSaveResult> SaveAsync(
        IReadOnlyList<ControllerProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var issues = _validator.Validate(profiles);
        if (ProfileValidator.HasErrors(issues))
        {
            return new ProfileSaveResult(Saved: false, issues);
        }

        await _store.SaveAsync(profiles, cancellationToken);
        return new ProfileSaveResult(Saved: true, issues);
    }

    /// <summary>从文件导入 Profile（校验失败时抛出 <see cref="ProfileStoreException"/>）。</summary>
    public async ValueTask<IReadOnlyList<ControllerProfile>> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        await _store.ImportAsync(sourcePath, cancellationToken);

    /// <summary>将 Profile 集合导出到目标路径（校验失败时抛出 <see cref="ProfileStoreException"/>）。</summary>
    public async ValueTask ExportAsync(
        IReadOnlyList<ControllerProfile> profiles,
        string targetPath,
        CancellationToken cancellationToken = default) =>
        await _store.ExportAsync(profiles, targetPath, cancellationToken);

    /// <summary>创建带默认值的新 Profile（不落盘，需调用 <see cref="SaveAsync"/>）。</summary>
    public static ControllerProfile CreateProfile(
        string name = "新配置",
        bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ControllerProfile(
            Guid.NewGuid(),
            name.Trim(),
            Priority: 0,
            isDefault,
            AppRules: [],
            Bindings: []);
    }

    /// <summary>创建新的 Binding（不落盘；动作默认留空，待 UI 补全后经校验保存）。</summary>
    public static InputBinding CreateBinding(
        string controlId,
        InputGesture gesture = InputGesture.Pressed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
        return new InputBinding(
            Guid.NewGuid(),
            new ControllerTrigger(controlId, gesture),
            new KeyboardShortcutAction(Keys: []),
            Feedback: null);
    }
}