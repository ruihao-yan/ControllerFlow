using ControllerFlow.Core.Models;
using ControllerFlow.Core.Profiles;
using Xunit;

namespace ControllerFlow.Core.Tests;

public sealed class JsonProfileStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "controllerflow-tests",
        Guid.NewGuid().ToString("N"));

    private JsonProfileStore CreateStore(string? relativePath = null) =>
        new(Path.Combine(_tempDirectory, relativePath ?? "profiles.json"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public async Task LoadAsync_FileMissing_ReturnsEmpty()
    {
        var store = CreateStore();

        var profiles = await store.LoadAsync();

        Assert.Empty(profiles);
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacySpeechHotkeyAndCreatesBackup()
    {
        var path = Path.Combine(_tempDirectory, "profiles.json");
        Directory.CreateDirectory(_tempDirectory);
        var json =
            """
            {
              "version": 1,
              "profiles": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "name": "旧配置",
                  "priority": 0,
                  "isDefault": true,
                  "appRules": [],
                  "bindings": [
                    {
                      "id": "22222222-2222-2222-2222-222222222222",
                      "trigger": { "controlId": "RB", "gesture": "pressed" },
                      "action": {
                        "type": "speechTool",
                        "start": { "type": "keyboardShortcut", "keys": ["Ctrl", "Space"], "keyDownOnly": true },
                        "stop": { "type": "keyboardShortcut", "keys": ["Ctrl", "Space"] }
                      }
                    }
                  ]
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, json);

        var store = CreateStore();
        var profiles = await store.LoadAsync();

        var bindings = Assert.Single(profiles).Bindings;
        Assert.Equal(2, bindings.Count);

        var start = bindings[0];
        Assert.Equal(InputGesture.Pressed, start.Trigger.Gesture);
        var startAction = Assert.IsType<KeyboardShortcutAction>(start.Action);
        Assert.Equal(["Ctrl", "Space"], startAction.Keys);
        Assert.True(startAction.KeyDownOnly);

        var stop = bindings[1];
        Assert.Equal(InputGesture.Released, stop.Trigger.Gesture);
        Assert.Equal(["Ctrl", "Space"], Assert.IsType<KeyboardShortcutAction>(stop.Action).Keys);
        Assert.NotEqual(start.Id, stop.Id);
        Assert.NotEmpty(store.LastMigrationWarnings);
        Assert.Single(Directory.GetFiles(_tempDirectory, "*.before-speech-removal.json"));
    }

    [Fact]
    public async Task LoadAsync_DisablesLegacySpeechProcessAction()
    {
        var path = Path.Combine(_tempDirectory, "profiles.json");
        Directory.CreateDirectory(_tempDirectory);
        var json =
            """
            {
              "version": 1,
              "profiles": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "name": "旧配置",
                  "priority": 0,
                  "isDefault": true,
                  "appRules": [],
                  "bindings": [
                    {
                      "id": "22222222-2222-2222-2222-222222222222",
                      "trigger": { "controlId": "RB", "gesture": "pressed" },
                      "action": {
                        "type": "speechTool",
                        "start": { "type": "keyboardShortcut", "keys": [] },
                        "stop": { "type": "keyboardShortcut", "keys": [] },
                        "executablePath": "C:/tools/stt.exe"
                      }
                    }
                  ]
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, json);

        var profiles = await CreateStore().LoadAsync();
        var binding = Assert.Single(Assert.Single(profiles).Bindings);

        Assert.False(binding.Enabled);
        Assert.Equal(["F24"], Assert.IsType<KeyboardShortcutAction>(binding.Action).Keys);
    }

    [Fact]
    public async Task LoadAsync_DisablesLegacySpeechHotkeyWhenShortcutIsEmpty()
    {
        var path = Path.Combine(_tempDirectory, "profiles.json");
        Directory.CreateDirectory(_tempDirectory);
        var json =
            """
            {
              "version": 1,
              "profiles": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "name": "旧配置",
                  "priority": 0,
                  "isDefault": true,
                  "appRules": [],
                  "bindings": [
                    {
                      "id": "22222222-2222-2222-2222-222222222222",
                      "trigger": { "controlId": "RB", "gesture": "pressed" },
                      "action": {
                        "type": "speechTool",
                        "start": { "type": "keyboardShortcut", "keys": [] },
                        "stop": { "type": "keyboardShortcut", "keys": ["Space"] }
                      }
                    }
                  ]
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, json);

        var profiles = await CreateStore().LoadAsync();
        var binding = Assert.Single(Assert.Single(profiles).Bindings);

        Assert.False(binding.Enabled);
        Assert.Equal(["F24"], Assert.IsType<KeyboardShortcutAction>(binding.Action).Keys);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsProfiles()
    {
        var store = CreateStore();
        var original = new[]
        {
            TestProfiles.DefaultProfile(
                "全局",
                TestProfiles.Binding("A", gesture: InputGesture.Held, holdMilliseconds: 80,
                    action: new KeyboardShortcutAction(["Ctrl", "Alt", "C"]),
                    feedback: new HapticPattern(0.3, 0.8, TimeSpan.FromMilliseconds(120))))
        };

        await store.SaveAsync(original);
        var loaded = await store.LoadAsync();

        var profile = Assert.Single(loaded);
        Assert.Equal(original[0].Id, profile.Id);
        Assert.Equal("全局", profile.Name);
        Assert.True(profile.IsDefault);

        var binding = Assert.Single(profile.Bindings);
        Assert.Equal(InputGesture.Held, binding.Trigger.Gesture);
        Assert.Equal(80, binding.Trigger.HoldMilliseconds);
        Assert.Equal(0.3, binding.Feedback!.LeftMotor);
        Assert.Equal(TimeSpan.FromMilliseconds(120), binding.Feedback.Duration);

        var keys = Assert.IsType<KeyboardShortcutAction>(binding.Action);
        Assert.Equal(["Ctrl", "Alt", "C"], keys.Keys);
    }

    [Fact]
    public async Task RoundTrip_AllActionTypes()
    {
        var store = CreateStore();
        var original = new[]
        {
            TestProfiles.DefaultProfile(
                TestProfiles.Binding("A", action: new KeyboardShortcutAction(["Ctrl", "S"])),
                TestProfiles.Binding("B", action: new MouseAction(MouseOperation.LeftClick)),
                TestProfiles.Binding("X", action: new MouseAction(MouseOperation.ScrollVertical, Amount: 240)),
                TestProfiles.Binding("Y", action: new MediaKeyAction(KeyCode.VolumeUp)),
                TestProfiles.Binding("LB", action: new LaunchApplicationAction(@"C:\tools\app.exe", "--fast")),
                TestProfiles.Binding("RB", action: new KeyboardShortcutAction(["Ctrl", "Space"])))
        };

        await store.SaveAsync(original);
        var loaded = await store.LoadAsync();

        var profile = Assert.Single(loaded);
        Assert.Equal(original[0].Bindings.Count, profile.Bindings.Count);

        Assert.IsType<KeyboardShortcutAction>(profile.Bindings[0].Action);
        Assert.Equal(MouseOperation.LeftClick, Assert.IsType<MouseAction>(profile.Bindings[1].Action).Operation);
        Assert.Equal(240, Assert.IsType<MouseAction>(profile.Bindings[2].Action).Amount);
        Assert.Equal(KeyCode.VolumeUp, Assert.IsType<MediaKeyAction>(profile.Bindings[3].Action).Key);
        Assert.Equal(@"C:\tools\app.exe", Assert.IsType<LaunchApplicationAction>(profile.Bindings[4].Action).ExecutablePath);
        Assert.Equal(["Ctrl", "Space"], Assert.IsType<KeyboardShortcutAction>(profile.Bindings[5].Action).Keys);
    }

    [Fact]
    public async Task Save_WritesCamelCaseAndTypeDiscriminators()
    {
        var store = CreateStore();
        var profiles = new[]
        {
            TestProfiles.DefaultProfile("全局", TestProfiles.Binding("A", action: new MediaKeyAction(KeyCode.VolumeMute)))
        };

        await store.SaveAsync(profiles);

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDirectory, "profiles.json"));
        Assert.Contains("\"version\"", json, StringComparison.Ordinal);
        Assert.Contains("\"isDefault\"", json, StringComparison.Ordinal);
        Assert.Contains("\"VolumeMute\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"mediaKey\"", json, StringComparison.Ordinal);

        // 临时文件应已清理（原子写）。
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "profiles.json.tmp")));
    }

    [Fact]
    public async Task Save_CreatesMissingDirectory()
    {
        var store = CreateStore("nested/deeper/profiles.json");

        await store.SaveAsync([TestProfiles.DefaultProfile()]);

        Assert.True(File.Exists(Path.Combine(_tempDirectory, "nested", "deeper", "profiles.json")));
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_Throws()
    {
        var path = Path.Combine(_tempDirectory, "profiles.json");
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(path, "{ 这不是 JSON");

        var store = CreateStore();

        var ex = await Assert.ThrowsAsync<ProfileStoreException>(() => store.LoadAsync().AsTask());
        Assert.Contains("不是有效的 JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_InvalidContent_ThrowsWithIssues()
    {
        var path = Path.Combine(_tempDirectory, "profiles.json");
        Directory.CreateDirectory(_tempDirectory);
        // 未知按键名的 binding 无法通过校验。
        var json =
            """
            {
              "version": 1,
              "profiles": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "name": "坏配置",
                  "priority": 0,
                  "isDefault": true,
                  "appRules": [],
                  "bindings": [
                    {
                      "id": "22222222-2222-2222-2222-222222222222",
                      "trigger": { "controlId": "A", "gesture": "pressed" },
                      "action": { "type": "keyboardShortcut", "keys": ["NoSuchKey"] }
                    }
                  ]
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, json);

        var store = CreateStore();

        var ex = await Assert.ThrowsAsync<ProfileStoreException>(() => store.LoadAsync().AsTask());
        Assert.Contains("校验失败", ex.Message, StringComparison.Ordinal);
        Assert.Contains(ex.Issues!, i => i.Message.Contains("NoSuchKey", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveAsync_InvalidProfiles_ThrowsAndDoesNotWrite()
    {
        var store = CreateStore();
        var invalid = new[]
        {
            TestProfiles.DefaultProfile("坏配置",
                TestProfiles.Binding("A", action: new KeyboardShortcutAction([])))
        };

        var ex = await Assert.ThrowsAsync<ProfileStoreException>(() => store.SaveAsync(invalid).AsTask());
        Assert.NotNull(ex.Issues);
        Assert.True(ProfileValidator.HasErrors(ex.Issues));
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "profiles.json")));
    }

    [Fact]
    public async Task ExportAndImport_RoundTrips()
    {
        var store = CreateStore();
        var profiles = new[]
        {
            TestProfiles.AppProfile("导出测试", new AppMatchRule(ExecutablePath: @"D:\Games\game.exe"))
        };
        var exportPath = Path.Combine(_tempDirectory, "export", "backup.json");

        await store.ExportAsync(profiles, exportPath);
        Assert.True(File.Exists(exportPath));

        var imported = await store.ImportAsync(exportPath);
        var loaded = Assert.Single(imported);
        Assert.Equal("导出测试", loaded.Name);
        Assert.Equal(@"D:\Games\game.exe", loaded.AppRules[0].ExecutablePath);
    }

    [Fact]
    public async Task ImportAsync_InvalidFile_Throws()
    {
        var path = Path.Combine(_tempDirectory, "bad.json");
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(path, "null:null");

        var store = CreateStore();

        var ex = await Assert.ThrowsAsync<ProfileStoreException>(() => store.ImportAsync(path).AsTask());
        Assert.Contains("不是有效的 JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_EmptyDocument_ReturnsEmpty()
    {
        var path = Path.Combine(_tempDirectory, "empty.json");
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(path, "{}");

        var store = CreateStore();

        var imported = await store.ImportAsync(path);
        Assert.Empty(imported);
    }
}