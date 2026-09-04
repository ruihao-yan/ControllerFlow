# AGENTS.md - ControllerFlow 开发约定

本文件是给 AI 编码助手（opencode / pi / Claude Code 等）的项目级指令，所有代码改动必须遵守。

## 代码要求

1. **禁止使用任何 icons** — 代码、XAML、UI 中一律不得使用图标（字体图标、SVG 图标、图片图标等均不允许）。界面只用文字与布局表达。

2. **描述简洁明了，禁止"不是 xxx，而是 xxx"句式** — 注释、文档、提交信息、README 中不要使用"不是……而是……"这类对比说明，直接陈述事实，一句话说清楚。

3. **代码尽量复用，但不要有多余的抽象** —
   - 可以复用的公共逻辑（如按键名映射、校验逻辑）优先提取复用。
   - 不要为了"可能以后会复用"而提前抽象。
   - **不确定两个地方是否该共用同一逻辑时，分别独立编写**，不要强行合并。

## 项目结构

- `src/ControllerFlow.Core` — 领域模型、端口、路由、引擎；**不依赖 Windows API**
- `src/ControllerFlow.Windows` — Windows 能力封装（Gamepad、Win32、Haptics、Speech、托盘等）
- `src/ControllerFlow.App` — WPF 桌面应用
- `tests/` — 单元测试（Core / Windows）

## 质量标准

- `TreatWarningsAsErrors` 已开启：构建必须 0 警告 0 错误
- Core 与 Core.Tests 必须在 Linux 可构建、可测试：
  ```bash
  dotnet build ControllerFlow.sln
  dotnet test ControllerFlow.sln
  ```
- 改动后必须运行测试，全部通过才算完成。