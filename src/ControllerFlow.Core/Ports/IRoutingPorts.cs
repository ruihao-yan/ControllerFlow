using ControllerFlow.Core.Models;

namespace ControllerFlow.Core.Ports;

public interface IForegroundAppProvider
{
    ValueTask<ForegroundApp?> GetCurrentAsync(CancellationToken cancellationToken);
}

public interface IProfileRepository
{
    ValueTask<IReadOnlyList<ControllerProfile>> GetEnabledAsync(
        CancellationToken cancellationToken);
}

public interface IActionExecutor
{
    ValueTask ExecuteAsync(OutputAction action, CancellationToken cancellationToken);
}

public interface IHapticFeedback
{
    ValueTask PlayAsync(
        string deviceId,
        HapticPattern pattern,
        CancellationToken cancellationToken);
}
