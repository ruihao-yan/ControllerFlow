using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ControllerFlow.Core.Input;
using ControllerFlow.Core.Models;
using ControllerFlow.Core.Monitoring;
using ControllerFlow.Core.Profiles;
using ControllerFlow.Windows.Desktop;
using ControllerFlow.Windows.Diagnostics;
using ControllerFlow.Windows.Logging;

// 与 WPF 自身类型（System.Windows.Input.InputGesture / InputBinding / MouseAction）重名，使用别名消歧。
using CoreInputGesture = ControllerFlow.Core.Models.InputGesture;
using CoreInputBinding = ControllerFlow.Core.Models.InputBinding;
using CoreMouseAction = ControllerFlow.Core.Models.MouseAction;

namespace ControllerFlow.App;

public partial class MainWindow : Window
{
    private const int WmLeftButtonUp = 0x0202;
    private const int WmRightButtonUp = 0x0205;

    private static readonly string[] MediaKeyNames =
    [
        "VolumeUp", "VolumeDown", "VolumeMute",
        "MediaPlayPause", "MediaNextTrack", "MediaPreviousTrack", "MediaStop",
        "BrowserBack", "BrowserForward", "BrowserHome"
    ];

    private sealed record GestureOption(string Label, CoreInputGesture Gesture);
    private sealed record MouseOperationOption(string Label, MouseOperation Operation);

    private static readonly GestureOption[] GestureOptions =
    [
        new("按下", CoreInputGesture.Pressed),
        new("释放", CoreInputGesture.Released),
        new("长按", CoreInputGesture.Held)
    ];

    private static readonly MouseOperationOption[] MouseOperationOptions =
    [
        new("左键单击", MouseOperation.LeftClick),
        new("右键单击", MouseOperation.RightClick),
        new("中键单击", MouseOperation.MiddleClick),
        new("垂直滚动", MouseOperation.ScrollVertical),
        new("水平滚动", MouseOperation.ScrollHorizontal),
        new("相对移动", MouseOperation.Move)
    ];

    private readonly AppServices _services;
    private readonly List<ControllerProfile> _profiles = [];
    private readonly List<CoreInputBinding> _workingBindings = [];
    private readonly ObservableCollection<string> _bindingSummaries = [];
    private readonly List<AppRuleRow> _workingRules = [];

    private TrayIcon? _trayIcon;
    private HwndSource? _hwndSource;
    private System.Drawing.Icon? _appIcon;
    private IntPtr _iconHandle => _appIcon?.Handle ?? IntPtr.Zero;
    private CancellationTokenSource? _captureCts;
    private ControllerProfile? _editingProfile;
    private bool _exiting;
    private bool _capturing;
    private bool _keyboardShortcutCapturing;
    private bool _syncingUi;
    private readonly HashSet<KeyCode> _keyboardCaptureKeys = [];
    private string _keyboardShortcutBeforeCapture = string.Empty;
    private System.Windows.Threading.DispatcherTimer? _gamepadTimer;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        BindingsListBox.ItemsSource = _bindingSummaries;

        TriggerControlBox.ItemsSource = GamepadControls.All;
        TriggerGestureBox.ItemsSource = GestureOptions;
        TriggerGestureBox.DisplayMemberPath = "Label";
        ActionTypeBox.ItemsSource = _actionTypes;
        MouseOperationBox.ItemsSource = MouseOperationOptions;
        MouseOperationBox.DisplayMemberPath = "Label";
        MediaKeyBox.ItemsSource = MediaKeyNames;
        ActionTypeBox.SelectedIndex = 0;

        _services.RoutingMonitor.ResolutionChanged += OnResolutionChanged;
        _services.InputSource.InputReceived += OnInputStatusChanged;
        StatusEngineText.Text = "引擎：运行中";

        _gamepadTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _gamepadTimer.Tick += (_, _) => UpdateGamepadStatus();
        _gamepadTimer.Start();

        _ = LoadProfilesAsync();
    }

    public async Task LoadProfilesAsync()
    {
        List<ControllerProfile> loaded;
        try
        {
            loaded = (await _services.Editor.LoadAsync()).ToList();
        }
        catch (Exception ex)
        {
            FileLog.Error("读取 Profile 失败。", ex);
            loaded = [];
            ShowValidationError($"读取配置失败：{ex.Message}");
        }

        _profiles.Clear();
        _profiles.AddRange(loaded);
        ProfileListBox.ItemsSource = _profiles;
        if (_profiles.Count > 0)
        {
            ProfileListBox.SelectedIndex = 0;
        }

        if (_services.Store is JsonProfileStore jsonStore
            && jsonStore.LastMigrationWarnings.Count > 0)
        {
            ValidationIssuesBox.Text = string.Join(
                Environment.NewLine,
                jsonStore.LastMigrationWarnings.Select(warning => $"[迁移] {warning}"));
            SaveStatusText.Text = "检测到旧语音动作，已按提示迁移或停用；保存前请检查并备份配置。";
        }
    }

    // ---------- Profile 选择与基本信息 ----------

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || ProfileListBox.SelectedItem is not ControllerProfile profile)
        {
            return;
        }

        try
        {
            SaveCurrentDraft();
        }
        catch (ArgumentException ex)
        {
            _syncingUi = true;
            ProfileListBox.SelectedItem = _editingProfile;
            _syncingUi = false;
            ShowValidationError(ex.Message);
            return;
        }

        profile = _profiles.First(item => item.Id == profile.Id);
        _editingProfile = profile;
        _syncingUi = true;
        try
        {
            ProfileNameBox.Text = profile.Name;
            ProfilePriorityBox.Text = profile.Priority.ToString(CultureInfo.InvariantCulture);
            ProfileDefaultBox.IsChecked = profile.IsDefault;
            ProfileEnabledBox.IsChecked = profile.Enabled;

            _workingRules.Clear();
            foreach (var rule in profile.AppRules)
            {
                _workingRules.Add(new AppRuleRow
                {
                    ProcessName = rule.ProcessName ?? string.Empty,
                    ExecutablePath = rule.ExecutablePath ?? string.Empty,
                    WindowTitlePattern = rule.WindowTitlePattern ?? string.Empty
                });
            }

            RulesItems.ItemsSource = null;
            RulesItems.ItemsSource = _workingRules;

            _workingBindings.Clear();
            _workingBindings.AddRange(profile.Bindings);
            RebuildBindingSummaries();
            BindingsListBox.SelectedIndex = -1;
            ClearBindingFields();
            ValidationIssuesBox.Clear();
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private void SaveCurrentDraft()
    {
        if (_editingProfile is null)
        {
            return;
        }

        var index = _profiles.FindIndex(profile => profile.Id == _editingProfile.Id);
        if (index >= 0)
        {
            _profiles[index] = BuildProfileFromFields(_profiles[index]);
        }
    }

    private void OnNewProfileClick(object sender, RoutedEventArgs e) =>
        AddOrReplaceProfile(ProfileEditorService.CreateProfile("新配置"));

    private void OnDuplicateProfileClick(object sender, RoutedEventArgs e)
    {
        if (ProfileListBox.SelectedItem is not ControllerProfile profile)
        {
            return;
        }

        profile = BuildProfileFromFields(profile);
        var copy = profile with
        {
            Id = Guid.NewGuid(),
            Name = profile.Name + "（副本）",
            Bindings = profile.Bindings.Select(binding => binding with { Id = Guid.NewGuid() }).ToArray()
        };
        AddOrReplaceProfile(copy);
    }

    private void AddOrReplaceProfile(ControllerProfile profile)
    {
        SaveCurrentDraft();
        _editingProfile = null;
        var index = _profiles.FindIndex(existing => existing.Id == profile.Id);
        if (index >= 0)
        {
            _profiles[index] = profile;
        }
        else
        {
            _profiles.Add(profile);
        }

        ProfileListBox.ItemsSource = null;
        ProfileListBox.ItemsSource = _profiles;
        ProfileListBox.SelectedItem = profile;
    }

    private void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (ProfileListBox.SelectedItem is not ControllerProfile profile)
        {
            return;
        }

        _editingProfile = null;
        _profiles.RemoveAll(item => item.Id == profile.Id);
        ProfileListBox.ItemsSource = null;
        ProfileListBox.ItemsSource = _profiles;
        ProfileListBox.SelectedIndex = _profiles.Count > 0 ? 0 : -1;
    }

    // ---------- 目标应用规则 ----------

    private void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        _workingRules.Add(new AppRuleRow());
        RulesItems.ItemsSource = null;
        RulesItems.ItemsSource = _workingRules;
    }

    private void OnDeleteRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AppRuleRow row })
        {
            _workingRules.Remove(row);
            RulesItems.ItemsSource = null;
            RulesItems.ItemsSource = _workingRules;
        }
    }

    private async void OnPickForegroundClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        try
        {
            SaveStatusText.Text = "请在 3 秒内切换到目标窗口…";
            await Task.Delay(TimeSpan.FromSeconds(3));
            var app = await _services.ForegroundProvider.GetCurrentAsync(CancellationToken.None);
            if (app is null || app.ProcessId == Environment.ProcessId)
            {
                ShowValidationError("请切换到目标软件后重新拾取。");
                return;
            }

            _workingRules.Add(new AppRuleRow
            {
                ProcessName = app.ProcessName,
                ExecutablePath = app.ExecutablePath ?? string.Empty
            });
            RulesItems.ItemsSource = null;
            RulesItems.ItemsSource = _workingRules;
            SaveStatusText.Text = $"已拾取：{app.ProcessName}（{app.WindowTitle}）";
        }
        catch (Exception ex)
        {
            FileLog.Error("拾取前台窗口失败。", ex);
            ShowValidationError($"拾取失败：{ex.Message}");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    // ---------- Binding 编辑 ----------

    private void OnAddBindingClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var binding = BuildBindingFromFields();
            if (binding is null)
            {
                return;
            }

            _workingBindings.Add(binding);
            RebuildBindingSummaries();
            BindingsListBox.SelectedIndex = _workingBindings.Count - 1;
        }
        catch (Exception ex)
        {
            ShowValidationError(ex.Message);
        }
    }

    private void OnUpdateBindingClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var binding = BuildBindingFromFields();
            if (binding is null)
            {
                return;
            }

            var index = BindingsListBox.SelectedIndex;
            if (index < 0 || index >= _workingBindings.Count)
            {
                ShowValidationError("请先在列表中选择要更新的 Binding。");
                return;
            }

            _workingBindings[index] = binding with { Id = _workingBindings[index].Id };
            RebuildBindingSummaries();
            BindingsListBox.SelectedIndex = index;
        }
        catch (Exception ex)
        {
            ShowValidationError(ex.Message);
        }
    }

    private void OnDeleteBindingClick(object sender, RoutedEventArgs e)
    {
        var index = BindingsListBox.SelectedIndex;
        if (index < 0 || index >= _workingBindings.Count)
        {
            return;
        }

        _workingBindings.RemoveAt(index);
        RebuildBindingSummaries();
        BindingsListBox.SelectedIndex = -1;
        ClearBindingFields();
    }

    private async void OnTestBindingClick(object sender, RoutedEventArgs e)
    {
        var index = BindingsListBox.SelectedIndex;
        if (index < 0 || index >= _workingBindings.Count)
        {
            ShowValidationError("请先选择要测试的 Binding。");
            return;
        }

        try
        {
            await _services.Executor.ExecuteAsync(_workingBindings[index].Action, CancellationToken.None);
            SaveStatusText.Text = $"已执行输出测试：{_bindingSummaries[index]}";
        }
        catch (Exception ex)
        {
            FileLog.Error("Binding 输出测试失败。", ex);
            ShowValidationError($"输出测试失败：{ex.Message}");
        }
    }

    private void OnBindingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || BindingsListBox.SelectedIndex < 0)
        {
            return;
        }

        var index = BindingsListBox.SelectedIndex;
        if (index >= _workingBindings.Count)
        {
            return;
        }

        LoadBindingFields(_workingBindings[index]);
    }

    private void LoadBindingFields(CoreInputBinding binding)
    {
        _syncingUi = true;
        try
        {
            ClearBindingFields();

            TriggerControlBox.SelectedItem = binding.Trigger.ControlId;
            TriggerGestureBox.SelectedItem = GestureOptions.FirstOrDefault(
                option => option.Gesture == binding.Trigger.Gesture);
            TriggerHoldBox.Text = binding.Trigger.HoldMilliseconds.ToString(CultureInfo.InvariantCulture);
            BindingEnabledBox.IsChecked = binding.Enabled;

            switch (binding.Action)
            {
                case KeyboardShortcutAction keyboard:
                    ActionTypeBox.SelectedItem = _actionTypes[0];
                    KeyboardKeysBox.Text = string.Join("+", keyboard.Keys);
                    KeyboardHoldUntilReleaseBox.IsChecked = keyboard.KeyDownOnly;
                    KeyboardKeyUpOnlyBox.IsChecked = keyboard.KeyUpOnly;
                    break;

                case CoreMouseAction mouse:
                    ActionTypeBox.SelectedItem = _actionTypes[1];
                    MouseOperationBox.SelectedItem = MouseOperationOptions.FirstOrDefault(
                        option => option.Operation == mouse.Operation);
                    MouseAmountBox.Text = mouse.Amount.ToString(CultureInfo.InvariantCulture);
                    break;

                case MediaKeyAction media:
                    ActionTypeBox.SelectedItem = _actionTypes[2];
                    MediaKeyBox.SelectedItem = KeyNameMap.GetDisplayName(media.Key);
                    break;

                case LaunchApplicationAction launch:
                    ActionTypeBox.SelectedItem = _actionTypes[3];
                    LaunchPathBox.Text = launch.ExecutablePath;
                    LaunchArgsBox.Text = launch.Arguments ?? string.Empty;
                    break;

            }

            if (binding.Feedback is { } feedback)
            {
                FeedbackLeftBox.Text = feedback.LeftMotor.ToString("0.###", CultureInfo.InvariantCulture);
                FeedbackRightBox.Text = feedback.RightMotor.ToString("0.###", CultureInfo.InvariantCulture);
                FeedbackDurationBox.Text = ((int)feedback.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private void ClearBindingFields()
    {
        TriggerControlBox.SelectedItem = null;
        TriggerGestureBox.SelectedItem = GestureOptions[0];
        TriggerHoldBox.Text = "0";
        BindingEnabledBox.IsChecked = true;

        KeyboardKeysBox.Clear();
        KeyboardHoldUntilReleaseBox.IsChecked = false;
        KeyboardKeyUpOnlyBox.IsChecked = false;
        MouseAmountBox.Text = "0";
        LaunchPathBox.Clear();
        LaunchArgsBox.Clear();

        FeedbackLeftBox.Clear();
        FeedbackRightBox.Clear();
        FeedbackDurationBox.Clear();
    }

    private void OnActionTypeChanged(object sender, SelectionChangedEventArgs e) => UpdateActionPanels();

    private void OnKeyboardKeyDownOnlyChecked(object sender, RoutedEventArgs e)
    {
        if (KeyboardKeyUpOnlyBox is not null)
        {
            KeyboardKeyUpOnlyBox.IsChecked = false;
        }
    }

    private void OnKeyboardKeyUpOnlyChecked(object sender, RoutedEventArgs e)
    {
        if (KeyboardHoldUntilReleaseBox is not null)
        {
            KeyboardHoldUntilReleaseBox.IsChecked = false;
        }
    }

    private void UpdateActionPanels()
    {
        KeyboardActionPanel.Visibility = ActionTypeBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        MouseActionPanel.Visibility = ActionTypeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        MediaActionPanel.Visibility = ActionTypeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        LaunchActionPanel.Visibility = ActionTypeBox.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClearFeedbackClick(object sender, RoutedEventArgs e)
    {
        FeedbackLeftBox.Clear();
        FeedbackRightBox.Clear();
        FeedbackDurationBox.Clear();
    }

    private CoreInputBinding? BuildBindingFromFields()
    {
        var controlId = TriggerControlBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(controlId))
        {
            ShowValidationError("请选择触发控件。");
            return null;
        }

        var gesture = (TriggerGestureBox.SelectedItem as GestureOption)?.Gesture ?? CoreInputGesture.Pressed;
        var hold = ParseInt(TriggerHoldBox.Text, 0, 10_000);
        var action = BuildActionFromFields();
        if (action is null)
        {
            return null;
        }

        var feedback = BuildFeedbackFromFields();
        return new CoreInputBinding(
            Guid.NewGuid(),
            new ControllerTrigger(controlId, gesture, hold),
            action,
            feedback,
            BindingEnabledBox.IsChecked ?? true);
    }

    private OutputAction? BuildActionFromFields()
    {
        try
        {
            switch (ActionTypeBox.SelectedIndex)
            {
                case 0:
                {
                    var keys = ParseKeyCombo(KeyboardKeysBox.Text);
                    return keys.Count > 0
                        ? new KeyboardShortcutAction(
                            keys,
                            KeyDownOnly: KeyboardHoldUntilReleaseBox.IsChecked == true,
                            KeyUpOnly: KeyboardKeyUpOnlyBox.IsChecked == true)
                        : Missing("请点击组合键输入框并按下键盘组合键。");
                }

                case 1:
                {
                    var operation = MouseOperationOptions[Math.Max(0, MouseOperationBox.SelectedIndex)].Operation;
                    return new CoreMouseAction(operation, ParseInt(MouseAmountBox.Text, -100_000, 100_000));
                }

                case 2:
                {
                    if (MediaKeyBox.SelectedItem is not string name || !KeyNameMap.TryGet(name, out var code))
                    {
                        return Missing("请选择媒体键。");
                    }

                    return new MediaKeyAction(code);
                }

                case 3:
                {
                    if (string.IsNullOrWhiteSpace(LaunchPathBox.Text))
                    {
                        return Missing("请输入程序路径。");
                    }

                    return new LaunchApplicationAction(LaunchPathBox.Text.Trim(), LaunchArgsBox.Text.Trim());
                }

                default:
                    return Missing("请选择动作类型。");
            }
        }
        catch (ArgumentException ex)
        {
            ShowValidationError(ex.Message);
            return null;
        }
    }

    private HapticPattern? BuildFeedbackFromFields()
    {
        var leftText = FeedbackLeftBox.Text.Trim();
        var rightText = FeedbackRightBox.Text.Trim();
        var durationText = FeedbackDurationBox.Text.Trim();
        if (leftText.Length == 0 && rightText.Length == 0 && durationText.Length == 0)
        {
            return null;
        }

        var left = double.TryParse(leftText, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : 0;
        var right = double.TryParse(rightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
        var duration = TimeSpan.FromMilliseconds(ParseInt(durationText, 0, 60_000));
        return new HapticPattern(Math.Clamp(left, 0, 1), Math.Clamp(right, 0, 1), duration);
    }

    private static IReadOnlyList<string> ParseKeyCombo(string text)
    {
        var parts = (text ?? string.Empty)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keys = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (!KeyNameMap.TryGet(part, out _))
            {
                throw new ArgumentException($"无法识别的按键名「{part}」。");
            }

            keys.Add(part);
        }

        return keys;
    }

    private void RebuildBindingSummaries()
    {
        _bindingSummaries.Clear();
        foreach (var binding in _workingBindings)
        {
            _bindingSummaries.Add(DescribeBinding(binding));
        }
    }

    private static string DescribeBinding(CoreInputBinding binding)
    {
        var gesture = binding.Trigger.Gesture switch
        {
            CoreInputGesture.Pressed => "按下",
            CoreInputGesture.Released => "释放",
            _ => $"长按({binding.Trigger.HoldMilliseconds}ms)"
        };
        var action = binding.Action switch
        {
            KeyboardShortcutAction keyboard =>
                $"{string.Join("+", keyboard.Keys)}{(keyboard.KeyDownOnly ? "（保持至释放）" : keyboard.KeyUpOnly ? "（仅抬起）" : string.Empty)}",
            CoreMouseAction mouse => $"鼠标:{mouse.Operation}",
            MediaKeyAction media => KeyNameMap.GetDisplayName(media.Key),
            LaunchApplicationAction launch => $"启动:{Path.GetFileName(launch.ExecutablePath)}",
            _ => "?"
        };
        var feedback = binding.Feedback is null ? string.Empty : " · 震动";
        var enabled = binding.Enabled ? string.Empty : "（禁用）";
        return $"{binding.Trigger.ControlId} {gesture} → {action}{feedback}{enabled}";
    }

    private static OutputAction? Missing(string message)
    {
        throw new ArgumentException(message);
    }

    private static int ParseInt(string? text, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException($"无效的数字「{text}」。");
        }

        return Math.Clamp(value, min, max);
    }

    // ---------- 保存 / 导入 / 导出 ----------

    private async void OnSaveAllClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ProfileListBox.SelectedItem is ControllerProfile selected)
            {
                var updated = BuildProfileFromFields(selected);
                var index = _profiles.FindIndex(profile => profile.Id == selected.Id);
                _profiles[index] = updated;

                // 用刚构建的数据刷新编辑区（规则行由 UI 双向绑定直接读取，无需回填）。
                _workingBindings.Clear();
                _workingBindings.AddRange(updated.Bindings);
                RebuildBindingSummaries();
            }

            var result = await _services.Editor.SaveAsync(_profiles);
            if (!result.Saved)
            {
                ShowValidation(result.Issues);
                return;
            }

            await _services.Repository.ReloadAsync(CancellationToken.None);
            var selectedId = _editingProfile?.Id;
            _syncingUi = true;
            try
            {
                ProfileListBox.ItemsSource = null;
                ProfileListBox.ItemsSource = _profiles;
                _editingProfile = _profiles.FirstOrDefault(profile => profile.Id == selectedId);
                ProfileListBox.SelectedItem = _editingProfile;
            }
            finally
            {
                _syncingUi = false;
            }
            SaveStatusText.Text = $"已保存（{DateTime.Now:HH:mm:ss}），共 {_profiles.Count} 个 Profile。";
            FileLog.Info($"保存 {_profiles.Count} 个 Profile 成功。");
            ShowValidation(result.Issues);
        }
        catch (Exception ex)
        {
            FileLog.Error("保存 Profile 失败。", ex);
            ShowValidationError($"保存失败：{ex.Message}");
        }
    }

    private ControllerProfile BuildProfileFromFields(ControllerProfile template)
    {
        var name = ProfileNameBox.Text.Trim();
        var priority = ParseInt(ProfilePriorityBox.Text, 0, 10_000);

        var rules = _workingRules
            .Where(row => !string.IsNullOrWhiteSpace(row.ProcessName)
                || !string.IsNullOrWhiteSpace(row.ExecutablePath)
                || !string.IsNullOrWhiteSpace(row.WindowTitlePattern))
            .Select(row => new AppMatchRule(
                NullIfBlank(row.ProcessName),
                NullIfBlank(row.ExecutablePath),
                NullIfBlank(row.WindowTitlePattern)))
            .ToArray();

        return new ControllerProfile(
            template.Id,
            name,
            priority,
            ProfileDefaultBox.IsChecked ?? false,
            rules,
            _workingBindings.ToArray(),
            ProfileEnabledBox.IsChecked ?? true);
    }

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入 Profile",
            Filter = "ControllerFlow 配置 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SaveCurrentDraft();
            var imported = await _services.Editor.ImportAsync(dialog.FileName);
            if (imported.Count == 0)
            {
                ShowValidationError("导入文件不包含任何 Profile。");
                return;
            }

            _editingProfile = null;
            foreach (var profile in imported)
            {
                var index = _profiles.FindIndex(existing => existing.Id == profile.Id);
                if (index >= 0)
                {
                    _profiles[index] = profile;
                }
                else
                {
                    _profiles.Add(profile);
                }
            }

            ProfileListBox.ItemsSource = null;
            ProfileListBox.ItemsSource = _profiles;
            ProfileListBox.SelectedIndex = 0;
            SaveStatusText.Text = $"已导入 {imported.Count} 个 Profile，保存后生效。";
            FileLog.Info($"从 {dialog.FileName} 导入 {imported.Count} 个 Profile。");
        }
        catch (Exception ex)
        {
            FileLog.Error("导入 Profile 失败。", ex);
            ShowValidationError($"导入失败：{ex.Message}");
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 Profile",
            Filter = "ControllerFlow 配置 (*.json)|*.json",
            FileName = $"controllerflow-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SaveCurrentDraft();
            await _services.Editor.ExportAsync(_profiles, dialog.FileName);
            SaveStatusText.Text = $"已导出到 {dialog.FileName}。";
            FileLog.Info($"导出 {_profiles.Count} 个 Profile 到 {dialog.FileName}。");
        }
        catch (Exception ex)
        {
            FileLog.Error("导出 Profile 失败。", ex);
            ShowValidationError($"导出失败：{ex.Message}");
        }
    }

    private void ShowValidation(IReadOnlyList<ProfileValidationIssue> issues)
    {
        if (issues.Count == 0)
        {
            ValidationIssuesBox.Clear();
            return;
        }

        ValidationIssuesBox.Text = string.Join(
            Environment.NewLine,
            issues.Select(issue => $"{(issue.Severity == ProfileValidationSeverity.Error ? "[错误]" : "[警告]")} {issue.Message}"));
    }

    private void ShowValidationError(string message)
    {
        ValidationIssuesBox.Text = message;
    }

    // ---------- 键盘快捷键捕获 ----------

    private void OnKeyboardShortcutGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _keyboardShortcutCapturing = true;
        _keyboardCaptureKeys.Clear();
        _keyboardShortcutBeforeCapture = KeyboardKeysBox.Text;
        _services.Engine.IsPaused = true;
        if (!_capturing)
        {
            StatusEngineText.Text = "引擎：已暂停（键盘快捷键录入中）";
            SaveStatusText.Text = "请按下键盘组合键，录入完成后会自动保存到输入框。";
        }
    }

    private void OnKeyboardShortcutLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        EndKeyboardShortcutCapture();
    }

    private void OnKeyboardShortcutPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_keyboardShortcutCapturing || e.IsRepeat)
        {
            return;
        }

        e.Handled = true;

        if (!TryGetKeyCode(e, out var keyCode))
        {
            ShowValidationError("无法识别当前键盘按键，请重新录入。");
            EndKeyboardShortcutCapture();
            return;
        }

        keyCode = KeyboardShortcutCapture.NormalizeModifier(keyCode);
        if (keyCode == KeyCode.Escape)
        {
            KeyboardKeysBox.Text = _keyboardShortcutBeforeCapture;
            SaveStatusText.Text = "已取消键盘快捷键录入。";
            EndKeyboardShortcutCapture();
            return;
        }

        _keyboardCaptureKeys.Add(keyCode);
        KeyboardKeysBox.Text = string.Join("+", KeyboardShortcutCapture.Format(_keyboardCaptureKeys));
        if (!KeyboardShortcutCapture.IsModifier(keyCode))
        {
            EndKeyboardShortcutCapture();
        }
    }

    private void OnKeyboardShortcutPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_keyboardShortcutCapturing)
        {
            return;
        }

        e.Handled = true;
        if (!TryGetKeyCode(e, out var keyCode))
        {
            return;
        }

        keyCode = KeyboardShortcutCapture.NormalizeModifier(keyCode);
        if (!_keyboardCaptureKeys.Contains(keyCode)
            || _keyboardCaptureKeys.Any(key => !KeyboardShortcutCapture.IsModifier(key)))
        {
            return;
        }

        KeyboardKeysBox.Text = string.Join("+", KeyboardShortcutCapture.Format(_keyboardCaptureKeys));
        EndKeyboardShortcutCapture();
    }

    private void OnClearKeyboardShortcutClick(object sender, RoutedEventArgs e)
    {
        KeyboardKeysBox.Clear();
        _keyboardCaptureKeys.Clear();
        if (!_capturing)
        {
            _services.Engine.IsPaused = false;
            StatusEngineText.Text = "引擎：运行中";
        }
    }

    private static bool TryGetKeyCode(KeyEventArgs e, out KeyCode keyCode)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0 || !Enum.IsDefined(typeof(KeyCode), virtualKey))
        {
            keyCode = KeyCode.None;
            return false;
        }

        keyCode = (KeyCode)virtualKey;
        return true;
    }

    private void EndKeyboardShortcutCapture()
    {
        if (!_keyboardShortcutCapturing)
        {
            return;
        }

        _keyboardShortcutCapturing = false;
        _keyboardCaptureKeys.Clear();
        if (!_capturing)
        {
            _services.Engine.IsPaused = false;
            StatusEngineText.Text = "引擎：运行中";
        }
    }

    // ---------- 手柄按键捕获 ----------

    private void OnCaptureTriggerClick(object sender, RoutedEventArgs e)
    {
        if (_capturing)
        {
            return;
        }

        _capturing = true;
        _services.Engine.IsPaused = true;
        StatusEngineText.Text = "引擎：已暂停（按键捕获中）";
        CaptureButtonState("捕获中…（按下任意手柄按键，30 秒超时）");
        SaveStatusText.Text = "按键捕获中：引擎已暂停，请按下目标按键…";

        _captureCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _services.InputSource.InputReceived += OnCaptureInput;
        _captureCts.Token.Register(() => Dispatcher.Invoke(EndCapture));
    }

    private void OnCaptureInput(object? sender, ControllerInputEvent inputEvent)
    {
        if (inputEvent.Gesture != CoreInputGesture.Pressed)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            TriggerControlBox.SelectedItem = inputEvent.ControlId;
            SaveStatusText.Text = $"已捕获：{inputEvent.ControlId}";
            EndCapture();
        });
    }

    private void EndCapture()
    {
        if (!_capturing)
        {
            return;
        }

        _capturing = false;
        _services.InputSource.InputReceived -= OnCaptureInput;
        _captureCts?.Dispose();
        _captureCts = null;
        _services.Engine.IsPaused = false;
        CaptureButtonState(null);
        StatusEngineText.Text = "引擎：运行中";
    }

    private void CaptureButtonState(string? text)
    {
        if (FindName("CaptureTriggerButton") is Button button)
        {
            button.Content = text ?? "捕获按键…";
        }
    }

    // ---------- 状态栏 ----------

    private void OnResolutionChanged(object? sender, ProfileResolutionChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var app = e.App;
            StatusAppText.Text = app is null
                ? "前台：—"
                : $"前台：{app.ProcessName} · {app.WindowTitle}";
            StatusProfileText.Text = e.Resolution.Profile is null
                ? "命中 Profile：—"
                : $"命中 Profile：{e.Resolution.Profile.Name}{(e.Resolution.UsedDefaultFallback ? "（默认兜底）" : string.Empty)}";
        });
    }

    private void OnInputStatusChanged(object? sender, ControllerInputEvent inputEvent)
    {
        if (inputEvent.Gesture == CoreInputGesture.Released)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            StatusControlText.Text = $"按键：{inputEvent.ControlId}";
            StatusGestureText.Text = $"触发：{FormatGesture(inputEvent.Gesture)}";
        });
    }

    private static string FormatGesture(CoreInputGesture gesture) => gesture switch
    {
        CoreInputGesture.Pressed => "按下",
        CoreInputGesture.Held => "长按",
        _ => gesture.ToString()
    };

    private void UpdateGamepadStatus()
    {
        try
        {
            var count = _services.InputSource.ConnectedGamepadCount;
            StatusGamepadText.Text = $"手柄：{count} 个已连接";
        }
        catch
        {
            StatusGamepadText.Text = "手柄：不可用";
        }
    }

    // ---------- 自检与日志 ----------

    private async void OnRunSelfCheckClick(object sender, RoutedEventArgs e)
    {
        SelfCheckHintText.Text = "自检运行中…";
        var results = await Task.Run(() => AppSelfCheck.RunAll(
            _services.Store,
            _services.ForegroundProvider,
            _services.DataDirectory));

        SelfCheckItems.ItemsSource = results;
        var failed = results.Count(item => !item.Passed);
        SelfCheckHintText.Text = failed == 0
            ? $"自检完成：{results.Count} 项全部通过（{DateTime.Now:HH:mm:ss}）。"
            : $"自检完成：{failed}/{results.Count} 项失败，请查看日志。";
    }

    private void OnOpenLogDirectoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(FileLog.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = FileLog.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            FileLog.Error("打开日志目录失败。", ex);
        }
    }

    // ---------- 托盘 ----------

    public void StartMinimizedToTray()
    {
        Show();
        Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        _appIcon = LoadAppIcon();
        _trayIcon = new TrayIcon(handle);
        _trayIcon.Add(_iconHandle, "ControllerFlow 手柄控制器");

        LogPathText.Text = $"日志：{FileLog.LogFilePath}";
        ConfigPathText.Text = $"Profile 配置文件：{_services.ProfilesFilePath}（可用任意文本编辑器手工修改，重启后生效）";
    }

    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty);
        }
        catch (Exception ex)
        {
            FileLog.Warn($"加载应用图标失败（托盘无图标）。{ex.Message}");
            return null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)TrayIcon.CallbackMessage)
        {
            handled = true;
            switch ((int)lParam)
            {
                case WmLeftButtonUp:
                    ShowMainWindow();
                    break;
                case WmRightButtonUp:
                    ShowTrayMenu();
                    break;
            }
        }
        else if (_trayIcon is not null && msg == (int)TrayIcon.TaskbarCreatedMessage)
        {
            // 资源管理器重启：重新注册托盘图标。
            _trayIcon.ReAdd(_iconHandle, "ControllerFlow 手柄控制器");
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ShowMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ShowTrayMenu()
    {
        var menu = new ContextMenu();

        var showItem = new MenuItem { Header = "显示主窗口" };
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(showItem);

        var startupItem = new MenuItem
        {
            Header = "开机自启（最小化启动）",
            IsCheckable = true,
            IsChecked = ControllerFlow.Windows.StartupRegistration.IsRegistered()
        };
        startupItem.Click += (_, _) =>
        {
            try
            {
                ControllerFlow.Windows.StartupRegistration.SetEnabled(startupItem.IsChecked);
                FileLog.Info($"开机自启已{(startupItem.IsChecked ? "启用" : "禁用")}。");
            }
            catch (Exception ex)
            {
                FileLog.Error("设置开机自启失败。", ex);
            }
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) =>
        {
            _exiting = true;
            _trayIcon?.Remove();
            Application.Current.Shutdown();
        };
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // 最小化即收进托盘，避免占用任务栏。
            Hide();
            FileLog.Info("已最小化到托盘。");
        }
    }

    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        // 关闭窗口 = 收进托盘；真正退出走托盘菜单「退出」。
        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _services.RoutingMonitor.ResolutionChanged -= OnResolutionChanged;
        _services.InputSource.InputReceived -= OnInputStatusChanged;
        _hwndSource?.RemoveHook(WndProc);
        _trayIcon?.Dispose();
        _gamepadTimer?.Stop();
        _appIcon?.Dispose();
        _appIcon = null;
        _captureCts?.Dispose();

        base.OnClosed(e);
    }

    private static readonly string[] _actionTypes = ["键盘快捷键", "鼠标", "媒体键", "启动程序"];
}