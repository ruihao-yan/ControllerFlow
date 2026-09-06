using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ControllerFlow.Core.Input;
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
    private readonly List<string> _lastMigrationWarnings = [];

    /// <summary>最近一次读取时发现的旧语音动作迁移提示。</summary>
    public IReadOnlyList<string> LastMigrationWarnings => _lastMigrationWarnings.ToArray();

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

        json = MigrateLegacySpeechActions(json, path);

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

    private string MigrateLegacySpeechActions(string json, string sourcePath)
    {
        _lastMigrationWarnings.Clear();

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        if (root is not JsonObject document
            || GetArray(document, "profiles") is not { } profiles)
        {
            return json;
        }

        var changed = false;
        foreach (var profileNode in profiles)
        {
            if (profileNode is not JsonObject profile
                || GetArray(profile, "bindings") is not { } bindings)
            {
                continue;
            }

            for (var index = 0; index < bindings.Count; index++)
            {
                if (bindings[index] is not JsonObject binding
                    || GetObject(binding, "action") is not { } action
                    || !string.Equals(GetString(action, "type"), "speechTool", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var start = GetObject(action, "start");
                var stop = GetObject(action, "stop");
                var executablePath = GetString(action, "executablePath");
                var trigger = GetObject(binding, "trigger");
                var triggerGesture = trigger is null ? null : GetString(trigger, "gesture");
                var canMigrate = string.IsNullOrWhiteSpace(executablePath)
                    && IsMigratableKeyboardAction(start)
                    && IsMigratableKeyboardAction(stop)
                    && GetBoolean(stop!, "keyDownOnly") != true
                    && (string.IsNullOrWhiteSpace(triggerGesture)
                        || string.Equals(triggerGesture, "Pressed", StringComparison.OrdinalIgnoreCase));

                if (canMigrate)
                {
                    SetProperty(binding, "action", start!.DeepClone());

                    var releaseBinding = (JsonObject)binding.DeepClone();
                    SetProperty(releaseBinding, "id", JsonValue.Create(Guid.NewGuid().ToString()));
                    SetProperty(releaseBinding, "action", stop!.DeepClone());
                    var releaseTrigger = GetObject(releaseBinding, "trigger")!;
                    SetProperty(releaseTrigger, "gesture", JsonValue.Create("Released"));
                    bindings.Insert(index + 1, releaseBinding);
                    index++;

                    _lastMigrationWarnings.Add(
                        "已将旧语音快捷键动作迁移为按下与释放两条普通键盘 Binding。请检查按键映射。");
                }
                else
                {
                    SetProperty(binding, "enabled", JsonValue.Create(false));
                    SetProperty(binding, "action", CreateDisabledKeyboardAction());
                    _lastMigrationWarnings.Add(
                        string.IsNullOrWhiteSpace(executablePath)
                            ? "旧语音动作缺少可迁移的快捷键，已停用该 Binding。"
                            : $"旧语音工具动作（{executablePath}）无法迁移，已停用该 Binding。");
                }

                changed = true;
            }
        }

        if (!changed)
        {
            return json;
        }

        if (string.Equals(sourcePath, _filePath, StringComparison.OrdinalIgnoreCase))
        {
            TryCreateLegacyBackup(sourcePath);
        }

        return document.ToJsonString();
    }

    private void TryCreateLegacyBackup(string sourcePath)
    {
        var backupPath = $"{sourcePath}.before-speech-removal.json";
        try
        {
            if (!File.Exists(backupPath))
            {
                File.Copy(sourcePath, backupPath, overwrite: false);
            }

            _lastMigrationWarnings.Add($"旧配置备份已保留：{backupPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _lastMigrationWarnings.Add($"旧配置备份失败：{ex.Message}");
        }
    }

    private static JsonObject CreateDisabledKeyboardAction()
    {
        var keys = new JsonArray();
        keys.Add(JsonValue.Create("F24"));
        return new JsonObject
        {
            ["type"] = "keyboardShortcut",
            ["keys"] = keys
        };
    }

    private static bool IsMigratableKeyboardAction(JsonObject? action)
    {
        if (action is null
            || !string.Equals(GetString(action, "type"), "keyboardShortcut", StringComparison.OrdinalIgnoreCase)
            || GetArray(action, "keys") is not { Count: > 0 } keys
            || (GetBoolean(action, "keyDownOnly") == true && GetBoolean(action, "keyUpOnly") == true))
        {
            return false;
        }

        return keys.All(key =>
            key is JsonValue value
            && value.TryGetValue<string>(out var name)
            && KeyNameMap.TryGet(name, out _));
    }

    private static JsonArray? GetArray(JsonObject value, string name) =>
        GetProperty(value, name) as JsonArray;

    private static JsonObject? GetObject(JsonObject value, string name) =>
        GetProperty(value, name) as JsonObject;

    private static string? GetString(JsonObject value, string name) =>
        GetProperty(value, name) is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var result)
                ? result
                : null;

    private static bool? GetBoolean(JsonObject value, string name) =>
        GetProperty(value, name) is JsonValue jsonValue
            && jsonValue.TryGetValue<bool>(out var result)
                ? result
                : null;

    private static JsonNode? GetProperty(JsonObject value, string name) =>
        value.FirstOrDefault(property =>
            string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static void SetProperty(JsonObject value, string name, JsonNode? propertyValue)
    {
        var existingName = value
            .Select(property => property.Key)
            .FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
        value[existingName ?? name] = propertyValue;
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
