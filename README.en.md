# ControllerFlow

A Windows gamepad controller app built with C# and WPF.

Core data flow:

`Controller Input -> Router (foreground App + user Profile) -> Output -> Haptic Feedback`

## Key features

- Users create profiles, pick a target app, and configure gamepad key mappings.
- Gamepad input goes through a Router that matches the foreground app to a profile, falling back to a default profile when there is no match.
- Output actions include keyboard chords, mouse, media keys, and program launch.
- Haptic feedback fires after mapped actions, with per-binding feedback parameters.
- The profile switches automatically when the foreground app changes.

## Status

1. **Project skeleton and core boundaries** — WPF App, Core, Windows Infrastructure, Core/Windows Tests projects; domain models, port interfaces, profile routing
2. **Gamepad input layer** — XInput is preferred with `Windows.Gaming.Input` fallback (`ControllerFlow.Windows/Input`); Core `GamepadInputTracker` normalizes press/release/hold events with dead zone, debounce, and repeat handling
3. **Foreground app and Router** — Win32 foreground window lookup (process name/path/title), `AppRuleMatcher` regex matching, automatic profile switching on foreground change
4. **User mappings and output layer** — Profile/Binding JSON read/write, validation, import/export (`Profiles/JsonProfileStore`, `ProfileValidator`, `ProfileEditorService`), keyboard chords, mouse, media keys, program launch outputs (`Output/Win32ActionExecutor`), press/release pairing
5. **Configuration migration** — legacy speech actions are converted when possible; unsupported bindings are disabled and backed up
6. **Haptic feedback** — success/no-match/execution-failure feedback, per-binding intensity and duration overrides (`Haptics/GamepadHapticFeedback`)
7. **Desktop experience** — profile editing, key capture, target app picking, tray icon (`Desktop/TrayIcon`), startup registration (`StartupRegistration`), self-check (`Diagnostics/AppSelfCheck`), logging (`Logging/FileLog`)
8. **Delivery** — unit tests (139 Core + 6 Windows, all passing), publish script `scripts/publish-win-x64.ps1`

中文版：[README.md](README.md)

## Tests

```bash
dotnet test ControllerFlow.sln
```

- ControllerFlow.Core.Tests: routing, input normalization, configuration migration, and keyboard capture tests
- ControllerFlow.Windows.Tests: Windows capability and hardware diagnostics tests

## Tech baseline

- .NET 8
- WPF (net8.0-windows10.0.19041.0)
- Core layer has no dependency on Windows APIs; Windows capabilities live in `ControllerFlow.Windows`
- User configuration is persisted as JSON

## Local run requirements

Requires Windows 10 19041 or later and the .NET 8 SDK:

```powershell
dotnet restore ControllerFlow.sln
dotnet build ControllerFlow.sln
dotnet run --project src/ControllerFlow.App
```

## Publish

```powershell
# Produces a self-contained x64 installer
./scripts/publish-win-x64.ps1
```