namespace ControllerFlow.Core.Models;

public sealed record ForegroundApp(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string WindowTitle);

public sealed record AppMatchRule(
    string? ProcessName = null,
    string? ExecutablePath = null,
    string? WindowTitlePattern = null);

public sealed record HapticPattern(
    double LeftMotor,
    double RightMotor,
    TimeSpan Duration);

public sealed record InputBinding(
    Guid Id,
    ControllerTrigger Trigger,
    OutputAction Action,
    HapticPattern? Feedback = null,
    bool Enabled = true);

public sealed record ControllerProfile(
    Guid Id,
    string Name,
    int Priority,
    bool IsDefault,
    IReadOnlyList<AppMatchRule> AppRules,
    IReadOnlyList<InputBinding> Bindings,
    bool Enabled = true);
