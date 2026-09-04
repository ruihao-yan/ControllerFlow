namespace ControllerFlow.Core.Models;

public enum InputGesture
{
    Pressed,
    Released,
    Held
}

public sealed record ControllerInputEvent(
    string DeviceId,
    string ControlId,
    InputGesture Gesture,
    DateTimeOffset OccurredAt);

public sealed record ControllerTrigger(
    string ControlId,
    InputGesture Gesture,
    int HoldMilliseconds = 0);
