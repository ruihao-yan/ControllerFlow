using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;
using ControllerFlow.Core.Profiles;

namespace ControllerFlow.Core.Tests;

/// <summary>手动推进时间的 TimeProvider，用于确定性测试长按 / 重复节流。</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;

    public ManualTimeProvider(DateTimeOffset start = default)
    {
        _ticks = start.Ticks;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public override DateTimeOffset GetUtcNow() => new(_ticks, TimeSpan.Zero);

    public void Advance(TimeSpan delta) => _ticks += delta.Ticks;
}

internal sealed class StubForegroundAppProvider(ForegroundApp? app) : IForegroundAppProvider
{
    public ValueTask<ForegroundApp?> GetCurrentAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(app);
}

internal sealed class StubProfileRepository(IReadOnlyList<ControllerProfile> profiles)
    : IProfileRepository
{
    public ValueTask<IReadOnlyList<ControllerProfile>> GetEnabledAsync(
        CancellationToken cancellationToken) => ValueTask.FromResult(profiles);
}

/// <summary>记录全部执行动作的 IActionExecutor；可注入异常模拟失败。</summary>
internal sealed class RecordingExecutor : IActionExecutor
{
    public List<OutputAction> Executed { get; } = [];

    public Exception? ThrowOnExecute { get; set; }

    public ValueTask ExecuteAsync(OutputAction action, CancellationToken cancellationToken)
    {
        if (ThrowOnExecute is not null)
        {
            throw ThrowOnExecute;
        }

        Executed.Add(action);
        return ValueTask.CompletedTask;
    }
}

/// <summary>记录全部震动调用；零时长 / 零强度模式由引擎自行过滤。</summary>
internal sealed class RecordingHaptic : IHapticFeedback
{
    public List<(string DeviceId, HapticPattern Pattern)> Played { get; } = [];

    public ValueTask PlayAsync(string deviceId, HapticPattern pattern, CancellationToken cancellationToken)
    {
        Played.Add((deviceId, pattern));
        return ValueTask.CompletedTask;
    }
}

/// <summary>记录会话启停的语音工具进程控制器；可按需注入启动失败。</summary>
internal sealed class RecordingSpeechTool : ISpeechToolProcessController
{
    public List<SpeechToolSession> Started { get; } = [];

    public List<SpeechToolSession> Stopped { get; } = [];

    public Exception? ThrowOnStart { get; set; }

    public ValueTask<SpeechToolSession?> StartAsync(
        string executablePath,
        string? arguments,
        CancellationToken cancellationToken)
    {
        if (ThrowOnStart is not null)
        {
            throw ThrowOnStart;
        }

        var session = new SpeechToolSession(Guid.NewGuid(), executablePath);
        Started.Add(session);
        return ValueTask.FromResult<SpeechToolSession?>(session);
    }

    public ValueTask StopAsync(SpeechToolSession session, CancellationToken cancellationToken)
    {
        Stopped.Add(session);
        return ValueTask.CompletedTask;
    }
}

/// <summary>内存 Profile 存储：可注入内容 / 记录写入次数，支持“文件不存在”语义。</summary>
internal sealed class InMemoryProfileStore : IProfileStore
{
    private readonly List<ControllerProfile>? _seed;
    private readonly List<ProfileValidationIssue>? _seedIssues;

    public List<ControllerProfile>? Current { get; set; }

    public int SaveCount { get; private set; }

    public bool FileExists { get; set; } = true;

    /// <param name="seed">初始内容；null 表示“文件不存在”。</param>
    /// <param name="seedIssues">初始内容校验问题；设置后 LoadAsync/ImportAsync 视为校验失败。</param>
    public InMemoryProfileStore(List<ControllerProfile>? seed = null, List<ProfileValidationIssue>? seedIssues = null)
    {
        _seed = seed;
        _seedIssues = seedIssues;
    }

    public ValueTask<IReadOnlyList<ControllerProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!FileExists)
        {
            return ValueTask.FromResult<IReadOnlyList<ControllerProfile>>([]);
        }

        if (_seedIssues is not null)
        {
            throw new ProfileStoreException("种子内容校验失败。") { Issues = _seedIssues };
        }

        // 保存后的 Current 优先于种子内容，模拟真实文件被改写。
        return ValueTask.FromResult<IReadOnlyList<ControllerProfile>>(Current ?? _seed ?? []);
    }

    public ValueTask SaveAsync(IReadOnlyList<ControllerProfile> profiles, CancellationToken cancellationToken)
    {
        Current = profiles.ToList();
        SaveCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask ExportAsync(
        IReadOnlyList<ControllerProfile> profiles,
        string targetPath,
        CancellationToken cancellationToken)
    {
        Current = profiles.ToList();
        LastExportPath = targetPath;
        SaveCount++;
        return ValueTask.CompletedTask;
    }

    public string? LastExportPath { get; private set; }

    public ValueTask<IReadOnlyList<ControllerProfile>> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ImportCallCount++;
        LastImportPath = sourcePath;
        return LoadAsync(cancellationToken);
    }

    public int ImportCallCount { get; private set; }

    public string? LastImportPath { get; private set; }
}

/// <summary>按脚本返回前台应用的 Provider（返回 null 表示“无前台窗口”）。</summary>
internal sealed class ScriptedForegroundAppProvider : IForegroundAppProvider
{
    private readonly Func<ForegroundApp?> _get;

    public int Calls { get; private set; }

    public ScriptedForegroundAppProvider(Func<ForegroundApp?> get)
    {
        _get = get;
    }

    public ValueTask<ForegroundApp?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return ValueTask.FromResult(_get());
    }
}

internal static class TestProfiles
{
    public static ControllerProfile DefaultProfile(params InputBinding[] bindings) =>
        new(Guid.NewGuid(), "默认", Priority: 0, IsDefault: true, AppRules: [], bindings);

    public static ControllerProfile DefaultProfile(string name, params InputBinding[] bindings) =>
        new(Guid.NewGuid(), name, Priority: 0, IsDefault: true, AppRules: [], bindings);

    public static ControllerProfile AppProfile(
        string name,
        AppMatchRule rule,
        int priority = 10,
        params InputBinding[] bindings) =>
        new(Guid.NewGuid(), name, priority, IsDefault: false, AppRules: [rule], bindings);

    public static InputBinding Binding(
        string controlId,
        InputGesture gesture = InputGesture.Pressed,
        OutputAction? action = null,
        HapticPattern? feedback = null,
        int holdMilliseconds = 0,
        bool enabled = true) =>
        new(
            Guid.NewGuid(),
            new ControllerTrigger(controlId, gesture, holdMilliseconds),
            action ?? new KeyboardShortcutAction(["Ctrl", "C"]),
            feedback,
            enabled);
}

/// <summary>轮询等待条件成立（测试用，避免忙等）。</summary>
internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("等待条件超时。");
            }

            await Task.Delay(5);
        }
    }
}