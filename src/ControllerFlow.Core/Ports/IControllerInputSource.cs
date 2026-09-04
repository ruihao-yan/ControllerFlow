using ControllerFlow.Core.Models;

namespace ControllerFlow.Core.Ports;

public interface IControllerInputSource
{
    event EventHandler<ControllerInputEvent>? InputReceived;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
