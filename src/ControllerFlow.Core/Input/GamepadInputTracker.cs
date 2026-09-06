using ControllerFlow.Core.Models;

namespace ControllerFlow.Core.Input;

/// <summary>手柄输入归一化参数。</summary>
public sealed class GamepadInputTrackerOptions
{
    /// <summary>按键去抖时间。状态必须稳定保持该时长后才确认按下，抑制机械抖动。</summary>
    public int DebounceMilliseconds { get; init; } = 15;

    /// <summary>长按判定阈值：按下保持达到该时长后开始产生 Held 事件。</summary>
    public int HoldThresholdMilliseconds { get; init; } = 400;

    /// <summary>长按期间 Held 事件的重复间隔。</summary>
    public int HoldRepeatIntervalMilliseconds { get; init; } = 50;

    /// <summary>摇杆死区。轴绝对值超过该值时生成对应方向控件。</summary>
    public double StickDeadzone { get; init; } = 0.12;

    /// <summary>扳机按键化阈值。扳机值达到该值时生成扳机控件。</summary>
    public double TriggerThreshold { get; init; } = 0.5;

    public static GamepadInputTrackerOptions Default { get; } = new();
}

/// <summary>
/// 将原始 <see cref="GamepadFrame"/> 序列归一化为按下 / 释放 / 长按事件：
/// 处理摇杆死区、扳机阈值、按键去抖与长按重复触发。
/// 不依赖 Windows API，通过 <see cref="TimeProvider"/> 注入时间以便测试。
/// </summary>
public sealed class GamepadInputTracker
{
    private enum ControlState
    {
        Inactive,
        Debouncing,
        Active
    }

    private sealed class ControlInfo
    {
        public ControlState State = ControlState.Inactive;
        public long StateChangedAt;
        public long LastHeldAt;
    }

    private readonly GamepadInputTrackerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, Dictionary<string, ControlInfo>> _devices = new(StringComparer.Ordinal);

    public GamepadInputTracker(
        GamepadInputTrackerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? GamepadInputTrackerOptions.Default;
        ValidateOptions(_options);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 处理一帧采样，返回本帧产生的事件（按下 / 释放 / 长按）。
    /// 轮询循环应保持稳定节奏调用（例如每 10ms），长按重复依赖后续帧推进时间。
    /// </summary>
    public IReadOnlyList<ControllerInputEvent> ProcessFrame(GamepadFrame frame, string deviceId)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var events = new List<ControllerInputEvent>();
        var now = _timeProvider.GetTimestamp();
        var activeControls = ComputeActiveControls(frame);

        if (!_devices.TryGetValue(deviceId, out var states))
        {
            states = new Dictionary<string, ControlInfo>(StringComparer.Ordinal);
            _devices.Add(deviceId, states);
        }

        // 1) 当前帧不再激活的控件：释放（去抖中的控件视为一次弹跳，静默回到未激活）。
        foreach (var (controlId, info) in states)
        {
            if (activeControls.Contains(controlId) || info.State == ControlState.Inactive)
            {
                continue;
            }

            if (info.State == ControlState.Debouncing)
            {
                info.State = ControlState.Inactive;
                continue;
            }

            events.Add(CreateEvent(deviceId, controlId, InputGesture.Released));
            info.State = ControlState.Inactive;
        }

        // 2) 当前帧激活的控件：去抖确认、长按重复。
        foreach (var controlId in activeControls)
        {
            if (!states.TryGetValue(controlId, out var info))
            {
                info = new ControlInfo();
                states.Add(controlId, info);
            }

            switch (info.State)
            {
                case ControlState.Inactive:
                    info.State = ControlState.Debouncing;
                    info.StateChangedAt = now;
                    break;

                case ControlState.Debouncing:
                    if (ElapsedMilliseconds(info.StateChangedAt, now) < _options.DebounceMilliseconds)
                    {
                        break;
                    }

                    info.State = ControlState.Active;
                    info.StateChangedAt = now;
                    info.LastHeldAt = now;
                    events.Add(CreateEvent(deviceId, controlId, InputGesture.Pressed));
                    break;

                case ControlState.Active:
                    var holdTicks = ToTimestampTicks(_options.HoldThresholdMilliseconds);
                    var firstHeldAt = info.StateChangedAt + holdTicks;
                    if (now < firstHeldAt)
                    {
                        break;
                    }

                    // 长按重复从长按阈值开始计时。此前按下到阈值之间的时间
                    // 不应在达到阈值时补发多次 Held，否则一次长按会瞬间执行多次快捷键。
                    var repeatTicks = ToTimestampTicks(_options.HoldRepeatIntervalMilliseconds);
                    if (info.LastHeldAt < firstHeldAt)
                    {
                        info.LastHeldAt = firstHeldAt;
                    }

                    var catchUpLimit = 64;
                    while (catchUpLimit-- > 0 && info.LastHeldAt <= now)
                    {
                        events.Add(CreateEvent(deviceId, controlId, InputGesture.Held));
                        info.LastHeldAt += repeatTicks;
                    }

                    break;
            }
        }

        return events;
    }

    /// <summary>
    /// 设备断开时重置其全部状态：为仍处于激活（已确认按下）的控件生成释放事件，
    /// 与 ProcessFrame 的弹跳静默语义一致（去抖中的未确认按下不产生任何事件）。
    /// </summary>
    public IReadOnlyList<ControllerInputEvent> Reset(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var events = new List<ControllerInputEvent>();
        if (!_devices.Remove(deviceId, out var states))
        {
            return events;
        }

        foreach (var (controlId, info) in states)
        {
            if (info.State == ControlState.Active)
            {
                events.Add(CreateEvent(deviceId, controlId, InputGesture.Released));
            }
        }

        return events;
    }

    private ISet<string> ComputeActiveControls(GamepadFrame frame)
    {
        var controls = new HashSet<string>(frame.PressedButtons, StringComparer.Ordinal);

        if (frame.LeftThumbX <= -_options.StickDeadzone)
        {
            controls.Add(GamepadControls.LeftStickLeft);
        }

        if (frame.LeftThumbX >= _options.StickDeadzone)
        {
            controls.Add(GamepadControls.LeftStickRight);
        }

        if (frame.LeftThumbY >= _options.StickDeadzone)
        {
            controls.Add(GamepadControls.LeftStickUp);
        }

        if (frame.LeftThumbY <= -_options.StickDeadzone)
        {
            controls.Add(GamepadControls.LeftStickDown);
        }

        if (frame.RightThumbX <= -_options.StickDeadzone)
        {
            controls.Add(GamepadControls.RightStickLeft);
        }

        if (frame.RightThumbX >= _options.StickDeadzone)
        {
            controls.Add(GamepadControls.RightStickRight);
        }

        if (frame.RightThumbY >= _options.StickDeadzone)
        {
            controls.Add(GamepadControls.RightStickUp);
        }

        if (frame.RightThumbY <= -_options.StickDeadzone)
        {
            controls.Add(GamepadControls.RightStickDown);
        }

        if (frame.LeftTrigger >= _options.TriggerThreshold)
        {
            controls.Add(GamepadControls.LeftTrigger);
        }

        if (frame.RightTrigger >= _options.TriggerThreshold)
        {
            controls.Add(GamepadControls.RightTrigger);
        }

        return controls;
    }

    private ControllerInputEvent CreateEvent(string deviceId, string controlId, InputGesture gesture) =>
        new(deviceId, controlId, gesture, _timeProvider.GetUtcNow());

    private long ToTimestampTicks(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return 0;
        }

        return Math.Max(
            1L,
            (long)(milliseconds / 1000.0 * _timeProvider.TimestampFrequency));
    }

    private int ElapsedMilliseconds(long start, long now) =>
        (int)_timeProvider.GetElapsedTime(start, now).TotalMilliseconds;

    private static void ValidateOptions(GamepadInputTrackerOptions options)
    {
        if (options.DebounceMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "DebounceMilliseconds 不能为负数。");
        }

        if (options.HoldThresholdMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "HoldThresholdMilliseconds 不能为负数。");
        }

        if (options.HoldRepeatIntervalMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "HoldRepeatIntervalMilliseconds 必须大于 0。");
        }

        if (options.StickDeadzone < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "StickDeadzone 不能为负数。");
        }

        if (options.TriggerThreshold <= 0 || options.TriggerThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "TriggerThreshold 必须在 (0, 1] 范围内。");
        }
    }
}
