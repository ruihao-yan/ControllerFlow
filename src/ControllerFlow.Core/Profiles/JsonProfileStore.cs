using System.Text.Json;
using System.Text.Json.Serialization;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;

namespace ControllerFlow.Core.Profiles;

/// <summary>Profile 存储异常（JSON 解析失败或校验失败）。</summary>
public sealed class ProfileStoreException : Exception
{
    public ProfileStoreException(string message)
        : base(message)
    {
    }

    public ProfileStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>校验失败时附带的具体问题列表。</summary>
    public IReadOnlyList<ProfileValidationIssue>? Issues { get; init; }
}

/// <summary>
/// Profile 文件格式：带版本号的文档，便于未来平滑升级。
/// </summary>
public sealed class ProfileFileFormat
{
    public int Version { get; init; } = 1;

    public IReadOnlyList<ControllerProfile> Profiles { get; init; } = [];
}

/// <summary>
/// Profile / Binding 的 JSON 读写实现（System.Text.Json，不依赖 Windows API）。
/// 写入采用临时文件 + 原子替换；所有读写均过 <see cref="ProfileValidator"/>。
/// </summary>
public sealed class JsonProfileStore : IProfileStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonProfileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async ValueTask<IReadOnlyList<ControllerProfile>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var document = await ReadDocumentAsync(_filePath, cancellationToken);
        return document.Profiles;
    }

    public async ValueTask SaveAsync(
        IReadOnlyList<ControllerProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var issues = new ProfileValidator().Validate(profiles);
        if (ProfileValidator.HasErrors(issues))
        {
            throw new ProfileStoreException("Profile 校验失败，未写入文件。") { Issues = issues };
        }

        await WriteDocumentAsync(
            new ProfileFileFormat { Version = 1, Profiles = profiles },
            _filePath,
            cancellationToken);
    }

    public async ValueTask ExportAsync(
        IReadOnlyList<ControllerProfile> profiles,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var issues = new ProfileValidator().Validate(profiles);
        if (ProfileValidator.HasErrors(issues))
        {
            throw new ProfileStoreException("导出失败：Profile 校验未通过。") { Issues = issues };
        }

        await WriteDocumentAsync(
            new ProfileFileFormat { Version = 1, Profiles = profiles },
            targetPath,
            cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ControllerProfile>> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var document = await ReadDocumentAsync(sourcePath, cancellationToken);
        return document.Profiles;
    }

    private async ValueTask<ProfileFileFormat> ReadDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProfileStoreException($"读取配置文件失败：{path}", ex);
        }

        ProfileFileFormat? document;
        try
        {
            document = JsonSerializer.Deserialize<ProfileFileFormat>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProfileStoreException($"配置文件不是有效的 JSON：{path}", ex);
        }

        var profiles = document?.Profiles ?? [];
        var issues = new ProfileValidator().Validate(profiles);
        if (ProfileValidator.HasErrors(issues))
        {
            throw new ProfileStoreException($"配置文件校验失败：{path}") { Issues = issues };
        }

        return document!;
    }

    private async ValueTask WriteDocumentAsync(
        ProfileFileFormat document,
        string path,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(document, _jsonOptions);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }
}
