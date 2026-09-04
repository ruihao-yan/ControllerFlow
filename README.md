# ControllerFlow

一个使用 C# 与 WPF 构建的 Windows 手柄控制器应用。

核心数据流：

`Controller Input -> Router（前台 App + 用户 Profile）-> Output -> Haptic Feedback`

## 当前完成度

PLAN.md 中的 8 个阶段均已实现：

1. **工程骨架与核心边界** — WPF App、Core、Windows Infrastructure、Core/Windows Tests 四个项目；领域模型、端口接口、Profile 路由
2. **手柄输入层** — `Windows.Gaming.Input` 读取（`ControllerFlow.Windows/Input`），Core 侧 `GamepadInputTracker` 统一为按下/释放/长按事件，含死区、抖动与重复触发处理
3. **前台应用与 Router** — Win32 前台窗口获取（进程名/路径/标题），`AppRuleMatcher` 正则匹配，前台切换时自动切换 Profile
4. **用户映射与输出层** — Profile/Binding JSON 读写、校验、导入导出（`Profiles/JsonProfileStore`、`ProfileValidator`、`ProfileEditorService`），键盘组合键、鼠标、媒体键、启动程序等输出（`Output/Win32ActionExecutor`），按下/释放配对
5. **语音方案** — 按住映射键调用本地语音转文字工具，松开结束（`Speech/SpeechToolProcessController` + Core 端口）
6. **震动反馈** — 成功/无匹配/执行失败三类反馈，支持每条 Binding 覆盖强度与时长（`Haptics/GamepadHapticFeedback`）
7. **桌面体验** — Profile 编辑、按键捕获、目标 App 拾取、托盘（`Desktop/TrayIcon`）、开机自启（`StartupRegistration`）、自检（`Diagnostics/AppSelfCheck`）、日志（`Logging/FileLog`）
8. **交付** — 单元测试（Core 139 个 + Windows 6 个，全部通过）、`scripts/publish-win-x64.ps1` 发布脚本

English version: [README.en.md](README.en.md)

## 测试

```bash
dotnet test ControllerFlow.sln
```

- ControllerFlow.Core.Tests：139 个测试
- ControllerFlow.Windows.Tests：6 个测试

## 技术基线

- .NET 8
- WPF（net8.0-windows10.0.19041.0）
- Core 层不依赖 Windows API，Windows 能力封装在 `ControllerFlow.Windows`
- 用户配置以 JSON 持久化

## 本地运行要求

需要 Windows 10 19041 或更高版本，以及 .NET 8 SDK：

```powershell
dotnet restore ControllerFlow.sln
dotnet build ControllerFlow.sln
dotnet run --project src/ControllerFlow.App
```

## 发布

```powershell
# 生成 self-contained x64 安装包
./scripts/publish-win-x64.ps1
```