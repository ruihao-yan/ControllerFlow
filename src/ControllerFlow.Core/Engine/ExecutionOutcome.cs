using ControllerFlow.Core.Models;
using ControllerFlow.Core.Routing;

namespace ControllerFlow.Core.Engine;

/// <summary>
/// 单次输入事件的处理结果。
/// </summary>
/// <param name="Status">路由状态：匹配、无 Profile、无绑定，或引擎暂停。</param>
/// <param name="Profile">命中的 Profile（未命中时为 null）。</param>
/// <param name="Binding">命中的 Binding（未命中时为 null）。</param>
/// <param name="ActionExecuted">是否实际执行了输出动作（如长按节流、按下/释放配对可能跳过执行）。</param>
/// <param name="Error">执行失败时的错误信息，成功时为 null。</param>
public sealed record ExecutionOutcome(
    RoutingStatus Status,
    ControllerProfile? Profile,
    InputBinding? Binding,
    bool ActionExecuted,
    string? Error = null);
