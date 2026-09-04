using ControllerFlow.Core.Models;

namespace ControllerFlow.Core.Routing;

public enum RoutingStatus
{
    Matched,
    NoProfile,
    NoBinding,
    Paused
}

public sealed record RoutingDecision(
    RoutingStatus Status,
    ControllerProfile? Profile = null,
    InputBinding? Binding = null);
