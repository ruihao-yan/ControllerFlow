using ControllerFlow.Core.Input;
using ControllerFlow.Core.Models;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class GamepadInputTrackerTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private GamepadInputTracker CreateTracker(
        int debounce = 15,
        int holdThreshold = 400,
        int repeatInterval = 100) =>
        new(
            new GamepadInputTrackerOptions
            {
                DebounceMilliseconds = debounce,
                HoldThresholdMilliseconds = holdThreshold,
                HoldRepeatIntervalMilliseconds = repeatInterval
            },
            _time);

    private static readonly IReadOnlySet<string> NoButtons = new HashSet<string>();

    [Fact]
    public void ProcessFrame_NoButtons_EmitsNothing()
    {
        var tracker = CreateTracker();

        var events = tracker.ProcessFrame(new GamepadFrame(NoButtons), "pad-1");

        Assert.Empty(events);
    }

    [Fact]
    public void ProcessFrame_Press_IsConfirmedAfterDebounce()
    {
        var tracker = CreateTracker(debounce: 15);

        // 首次出现：进入去抖，不产生事件。
        var first = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1");
        Assert.Empty(first);

        // 达到去抖时长后仍按住：确认按下。
        _time.Advance(TimeSpan.FromMilliseconds(15));
        var second = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1");
        var press = Assert.Single(second);
        Assert.Equal(GamepadControls.A, press.ControlId);
        Assert.Equal(InputGesture.Pressed, press.Gesture);
        Assert.Equal("pad-1", press.DeviceId);

        // 未达长按阈值：不产生 Held。
        _time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Empty(tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1"));
    }

    [Fact]
    public void ProcessFrame_Release_EmitsSingleReleased()
    {
        var tracker = CreateTracker();
        Press(tracker, "pad-1", GamepadControls.B);

        var events = tracker.ProcessFrame(Frame(), "pad-1");

        var release = Assert.Single(events);
        Assert.Equal(GamepadControls.B, release.ControlId);
        Assert.Equal(InputGesture.Released, release.Gesture);

        // 已释放后重复空帧不再产生事件。
        Assert.Empty(tracker.ProcessFrame(Frame(), "pad-1"));
    }

    [Fact]
    public void ProcessFrame_BounceWithinDebounce_IsSuppressed()
    {
        var tracker = CreateTracker(debounce: 15);

        _ = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1");
        _time.Advance(TimeSpan.FromMilliseconds(5));

        // 去抖窗口内弹起：静默回到未激活，无任何事件。
        Assert.Empty(tracker.ProcessFrame(Frame(), "pad-1"));

        // 再次按下并稳定超过去抖时长。
        _time.Advance(TimeSpan.FromMilliseconds(1));
        _ = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1");
        _time.Advance(TimeSpan.FromMilliseconds(15));
        var events = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1");

        var press = Assert.Single(events);
        Assert.Equal(InputGesture.Pressed, press.Gesture);
    }

    [Fact]
    public void ProcessFrame_LongHold_EmitsHeldAtRepeatInterval()
    {
        var tracker = CreateTracker(holdThreshold: 400, repeatInterval: 100);
        Press(tracker, "pad-1", GamepadControls.A);

        // 按下确认后满 400ms：只产生一次 Held，重复从长按阈值开始计时。
        _time.Advance(TimeSpan.FromMilliseconds(400));
        var first = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1");
        var held1 = Assert.Single(first);
        Assert.Equal(InputGesture.Held, held1.Gesture);

        // 未满重复间隔：无新事件。
        _time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Empty(tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1"));

        // 满 100ms：下一个 Held。
        _time.Advance(TimeSpan.FromMilliseconds(50));
        var second = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1");
        var held2 = Assert.Single(second);
        Assert.Equal(InputGesture.Held, held2.Gesture);

        // 释放：Released。
        var released = tracker.ProcessFrame(Frame(), "pad-1");
        Assert.Equal(InputGesture.Released, Assert.Single(released).Gesture);
    }

    [Fact]
    public void ProcessFrame_RbLongHold_EmitsFirstHeldOnce()
    {
        var tracker = CreateTracker(holdThreshold: 400, repeatInterval: 100);
        Press(tracker, "pad-1", GamepadControls.RightBumper);

        _time.Advance(TimeSpan.FromMilliseconds(400));
        var events = tracker.ProcessFrame(Frame(GamepadControls.RightBumper), "pad-1");

        var held = Assert.Single(events);
        Assert.Equal(GamepadControls.RightBumper, held.ControlId);
        Assert.Equal(InputGesture.Held, held.Gesture);
    }

    [Fact]
    public void ProcessFrame_LargeFrameGap_FillsMissingHeldEvents()
    {
        var tracker = CreateTracker(holdThreshold: 400, repeatInterval: 100);
        Press(tracker, "pad-1", GamepadControls.A);

        // 帧间隔不稳定：一次推进 950ms，应补齐 400/500/…/900 共 6 个 Held（有上限保护）。
        _time.Advance(TimeSpan.FromMilliseconds(950));
        var events = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1");

        Assert.Equal(6, events.Count);
        Assert.All(events, e => Assert.Equal(InputGesture.Held, e.Gesture));
    }

    [Fact]
    public void ProcessFrame_StickDeadzone_GeneratesDirections()
    {
        var tracker = CreateTracker();

        _ = tracker.ProcessFrame(
            new GamepadFrame(NoButtons, LeftThumbX: -0.5, LeftThumbY: 0.3, RightThumbX: 0.9, RightThumbY: -0.2),
            "pad-1");
        _time.Advance(TimeSpan.FromMilliseconds(15));
        var events = tracker.ProcessFrame(
            new GamepadFrame(NoButtons, LeftThumbX: -0.5, LeftThumbY: 0.3, RightThumbX: 0.9, RightThumbY: -0.2),
            "pad-1");

        var controls = events.Select(e => e.ControlId).ToHashSet();
        Assert.Contains(GamepadControls.LeftStickLeft, controls);
        Assert.Contains(GamepadControls.LeftStickUp, controls);
        Assert.Contains(GamepadControls.RightStickRight, controls);
        Assert.Contains(GamepadControls.RightStickDown, controls);

        // 死区内的微小扰动不产生方向。
        _ = tracker.ProcessFrame(
            new GamepadFrame(NoButtons, LeftThumbX: 0.05, LeftThumbY: -0.08),
            "pad-1");
        _time.Advance(TimeSpan.FromMilliseconds(15));
        Assert.Empty(tracker.ProcessFrame(
            new GamepadFrame(NoButtons, LeftThumbX: 0.05, LeftThumbY: -0.08),
            "pad-1"));
    }

    [Fact]
    public void ProcessFrame_TriggerThreshold_GeneratesTriggers()
    {
        var tracker = CreateTracker();

        _ = tracker.ProcessFrame(new GamepadFrame(NoButtons, LeftTrigger: 0.7, RightTrigger: 0.2), "pad-1");
        _time.Advance(TimeSpan.FromMilliseconds(15));
        var events = tracker.ProcessFrame(new GamepadFrame(NoButtons, LeftTrigger: 0.7, RightTrigger: 0.2), "pad-1");

        var controls = events.Select(e => e.ControlId).ToHashSet();
        Assert.Contains(GamepadControls.LeftTrigger, controls);
        Assert.DoesNotContain(GamepadControls.RightTrigger, controls);
    }

    [Fact]
    public void ProcessFrame_DigitalAndAnalog_Combine()
    {
        var tracker = CreateTracker();
        Press(tracker, "pad-1", GamepadControls.A);

        // 按钮与摇杆同时激活时全部上报。
        _ = tracker.ProcessFrame(
            new GamepadFrame(new HashSet<string> { GamepadControls.A }, LeftThumbX: 1.0),
            "pad-1");
        _time.Advance(TimeSpan.FromMilliseconds(400));
        var events = tracker.ProcessFrame(
            new GamepadFrame(new HashSet<string> { GamepadControls.A }, LeftThumbX: 1.0),
            "pad-1");

        var controls = events.Select(e => e.ControlId).ToHashSet();
        Assert.Contains(GamepadControls.LeftStickRight, controls);
        Assert.Contains(GamepadControls.A, controls);
    }

    [Fact]
    public void ProcessFrame_DevicesAreIsolated()
    {
        var tracker = CreateTracker();
        Press(tracker, "pad-1", GamepadControls.A);

        // 另一只手柄按下同键不会释放 pad-1 的状态。
        _ = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-2");
        _time.Advance(TimeSpan.FromMilliseconds(15));
        var pad2Events = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-2");

        Assert.Contains(pad2Events, e => e.DeviceId == "pad-2" && e.Gesture == InputGesture.Pressed);

        var pad1Events = tracker.ProcessFrame(Frame(), "pad-1");
        var release = Assert.Single(pad1Events);
        Assert.Equal("pad-1", release.DeviceId);
        Assert.Equal(InputGesture.Released, release.Gesture);
    }

    [Fact]
    public void Reset_EmitsReleasedForActiveControls()
    {
        var tracker = CreateTracker();
        Press(tracker, "pad-1", GamepadControls.A);

        var events = tracker.Reset("pad-1");

        var release = Assert.Single(events);
        Assert.Equal(InputGesture.Released, release.Gesture);

        // 再次 Reset 无状态可清。
        Assert.Empty(tracker.Reset("pad-1"));
    }

    [Fact]
    public void Reset_DebouncingControl_IsSilent()
    {
        var tracker = CreateTracker(debounce: 15);

        _ = tracker.ProcessFrame(Frame(GamepadControls.A), "pad-1");
        Assert.Empty(tracker.Reset("pad-1"));
    }

    [Fact]
    public void Ctor_InvalidOptions_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateTracker(debounce: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateTracker(repeatInterval: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GamepadInputTracker(new GamepadInputTrackerOptions { TriggerThreshold = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GamepadInputTracker(new GamepadInputTrackerOptions { TriggerThreshold = 1.5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GamepadInputTracker(new GamepadInputTrackerOptions { StickDeadzone = -0.1 }));
    }

    private static GamepadFrame Frame(params string[] controls) =>
        new(new HashSet<string>(controls, StringComparer.Ordinal));

    private void Press(GamepadInputTracker tracker, string deviceId, string controlId)
    {
        _ = tracker.ProcessFrame(Frame(controlId), deviceId);
        _time.Advance(TimeSpan.FromMilliseconds(15));
        _ = tracker.ProcessFrame(Frame(controlId), deviceId);
    }
}