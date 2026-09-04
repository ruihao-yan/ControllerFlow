using ControllerFlow.Core.Models;

namespace ControllerFlow.Core.Ports;

/// <summary>
/// Profile 持久化端口：负责整体读写 / 导入 / 导出，
/// 与 <see cref="IProfileRepository"/>（只读查询）分离。
/// </summary>
public interface IProfileStore
{
    /// <summary>加载全部 Profile（文件不存在时返回空列表；内容非法时抛出异常）。</summary>
    ValueTask<IReadOnlyList<ControllerProfile>> LoadAsync(
        CancellationToken cancellationToken = default);

    /// <summary>校验后保存全部 Profile（校验失败时不写入并抛出异常）。</summary>
    ValueTask SaveAsync(
        IReadOnlyList<ControllerProfile> profiles,
        CancellationToken cancellationToken = default);

    /// <summary>将指定 Profile 集合导出到目标路径。</summary>
    ValueTask ExportAsync(
        IReadOnlyList<ControllerProfile> profiles,
        string targetPath,
        CancellationToken cancellationToken = default);

    /// <summary>从文件导入 Profile（校验失败时抛出异常）。</summary>
    ValueTask<IReadOnlyList<ControllerProfile>> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
