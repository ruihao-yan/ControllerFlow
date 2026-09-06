using ControllerFlow.Core.Input;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Ports;
using Windows.Gaming.Input;

namespace ControllerFlow.Windows.Input;

/// <summary>
/// Windows 手柄输入源：优先使用 XInput，未枚举到设备时回退到 Windows.Gaming.Input。
/// 轮询全部已连接手柄（天然支持热插拔），把原始采样转换为
/// <see cref="GamepadFrame"/> 交给 Core 的 <see cref="GamepadInputTracker"/>
/// 归一化成按下 / 释放 / 长按事件。不同型号手柄统一按
/// <see cref="GamepadControls"/> 控件 ID 对外输出。
/// </summary>
public sealed class WindowsGamepadSource : IControllerInputSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private readonly GamepadInputTracker _tracker;
    private readonly object _sync = new();
    private readonly Dictionary<string, Gamepad> _active = new(StringComparer.Ordinal);
    private const int XInputMissingPollLimit = 100;

    private readonly HashSet<uint> _activeXInput = [];
    private int _xInputMissedPolls;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private int _nextDeviceId;

    public WindowsGamepadSource(GamepadInputTrackerOptions? options = null)
    {
        _tracker = new GamepadInputTracker(options);
    }

    public event EventHandler<ControllerInputEvent>? InputReceived;

    public int ConnectedGamepadCount
    {
        get
        {
            lock (_sync)
            {
                return _active.Count + _activeXInput.Count;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_pollTask is not null)
            {
                return _pollTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pollTask = Task.Run(() => PollAsync(_cts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? pollTask;
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _cts;
            _cts = null;
            cts?.Cancel();
            pollTask = _pollTask;
            _pollTask = null;
        }

        if (pollTask is null)
        {
            return;
        }

        try
        {
            await pollTask;
        }
        catch (OperationCanceledException)
        {
            // 预期路径：停止即取消轮询。
        }

        cts?.Dispose();
        ClearTrackedDevices();
    }

    private async Task PollAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                PollOnce();
                await Task.Delay(PollInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出。
        }
    }

    private void PollOnce()
    {
        if (PollXInputOnce())
        {
            RemoveMissingGamepads(new HashSet<Gamepad>());
            return;
        }

        Gamepad[] gamepads;
        try
        {
            gamepads = Gamepad.Gamepads.ToArray();
        }
        catch
        {
            gamepads = [];
        }

        var seen = new HashSet<Gamepad>();
        foreach (var gamepad in gamepads)
        {
            seen.Add(gamepad);
            var deviceId = GetOrRegisterDeviceId(gamepad);

            GamepadReading reading;
            try
            {
                reading = gamepad.GetCurrentReading();
            }
            catch
            {
                // 读取瞬间断开的手柄：跳过本次采样，下一轮按移除处理。
                continue;
            }

            Dispatch(_tracker.ProcessFrame(BuildFrame(reading), deviceId));
        }

        RemoveMissingGamepads(seen);
    }

    private bool PollXInputOnce()
    {
        var seen = new HashSet<uint>();
        for (uint index = 0; index < 4; index++)
        {
            if (!XInputNative.TryGetState(index, out var state))
            {
                continue;
            }

            seen.Add(index);
            lock (_sync)
            {
                _activeXInput.Add(index);
            }

            Dispatch(_tracker.ProcessFrame(
                XInputGamepadMapper.BuildFrame(state.Gamepad),
                $"xinput-{index}"));
        }

        if (seen.Count > 0)
        {
            _xInputMissedPolls = 0;
            RemoveMissingXInputGamepads(seen);
            return true;
        }

        lock (_sync)
        {
            if (_activeXInput.Count > 0)
            {
                _xInputMissedPolls++;
                if (_xInputMissedPolls < XInputMissingPollLimit)
                {
                    // 最多容忍约一秒的连续读取失败，保持当前按键会话，
                    // 避免无线链路抖动把一次按住拆成多次 Pressed。
                    return true;
                }
            }
        }

        _xInputMissedPolls = 0;
        RemoveMissingXInputGamepads(seen);
        return false;
    }

    private void RemoveMissingGamepads(ISet<Gamepad> seen)
    {
        List<ControllerInputEvent>? released = null;
        lock (_sync)
        {
            foreach (var entry in _active.ToList())
            {
                if (seen.Contains(entry.Value))
                {
                    continue;
                }

                _active.Remove(entry.Key);
                GamepadRegistry.Unregister(entry.Key);
                released ??= [];
                released.AddRange(_tracker.Reset(entry.Key));
            }
        }

        Dispatch(released);
    }

    private void RemoveMissingXInputGamepads(ISet<uint> seen)
    {
        List<ControllerInputEvent>? released = null;
        lock (_sync)
        {
            foreach (var index in _activeXInput.Where(index => !seen.Contains(index)).ToArray())
            {
                _activeXInput.Remove(index);
                released ??= [];
                released.AddRange(_tracker.Reset($"xinput-{index}"));
            }
        }

        Dispatch(released);
    }

    private void Dispatch(IEnumerable<ControllerInputEvent>? events)
    {
        if (events is null)
        {
            return;
        }

        foreach (var inputEvent in events)
        {
            InputReceived?.Invoke(this, inputEvent);
        }
    }

    private void ClearTrackedDevices()
    {
        lock (_sync)
        {
            foreach (var deviceId in _active.Keys)
            {
                GamepadRegistry.Unregister(deviceId);
                _ = _tracker.Reset(deviceId);
            }

            foreach (var index in _activeXInput)
            {
                _ = _tracker.Reset($"xinput-{index}");
            }

            _active.Clear();
            _activeXInput.Clear();
            _xInputMissedPolls = 0;
        }
    }

    private string GetOrRegisterDeviceId(Gamepad gamepad)
    {
        lock (_sync)
        {
            foreach (var entry in _active)
            {
                if (ReferenceEquals(entry.Value, gamepad))
                {
                    return entry.Key;
                }
            }

            var deviceId = $"gamepad-{_nextDeviceId++}";
            _active[deviceId] = gamepad;
            GamepadRegistry.Register(deviceId, gamepad);
            return deviceId;
        }
    }

    private static GamepadFrame BuildFrame(GamepadReading reading)
    {
        var buttons = new HashSet<string>(StringComparer.Ordinal);
        var flags = reading.Buttons;
        if ((flags & GamepadButtons.A) != 0)
        {
            buttons.Add(GamepadControls.A);
        }

        if ((flags & GamepadButtons.B) != 0)
        {
            buttons.Add(GamepadControls.B);
        }

        if ((flags & GamepadButtons.X) != 0)
        {
            buttons.Add(GamepadControls.X);
        }

        if ((flags & GamepadButtons.Y) != 0)
        {
            buttons.Add(GamepadControls.Y);
        }

        if ((flags & GamepadButtons.DPadUp) != 0)
        {
            buttons.Add(GamepadControls.DPadUp);
        }

        if ((flags & GamepadButtons.DPadDown) != 0)
        {
            buttons.Add(GamepadControls.DPadDown);
        }

        if ((flags & GamepadButtons.DPadLeft) != 0)
        {
            buttons.Add(GamepadControls.DPadLeft);
        }

        if ((flags & GamepadButtons.DPadRight) != 0)
        {
            buttons.Add(GamepadControls.DPadRight);
        }

        if ((flags & GamepadButtons.LeftShoulder) != 0)
        {
            buttons.Add(GamepadControls.LeftBumper);
        }

        if ((flags & GamepadButtons.RightShoulder) != 0)
        {
            buttons.Add(GamepadControls.RightBumper);
        }

        if ((flags & GamepadButtons.LeftThumbstick) != 0)
        {
            buttons.Add(GamepadControls.LeftStickClick);
        }

        if ((flags & GamepadButtons.RightThumbstick) != 0)
        {
            buttons.Add(GamepadControls.RightStickClick);
        }

        if ((flags & GamepadButtons.Menu) != 0)
        {
            buttons.Add(GamepadControls.Menu);
        }

        if ((flags & GamepadButtons.View) != 0)
        {
            buttons.Add(GamepadControls.View);
        }

        return new GamepadFrame(
            buttons,
            reading.LeftThumbstickX,
            reading.LeftThumbstickY,
            reading.RightThumbstickX,
            reading.RightThumbstickY,
            reading.LeftTrigger,
            reading.RightTrigger);
    }
}
