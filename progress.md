# Progress - ControllerFlow 项目进展

## 当前状态：完成（8 个阶段全部落地）

- 构建通过，0 警告 0 错误
- 测试通过：Core 139 个 + Windows 6 个，共 145 个
- 已推送 GitHub：https://github.com/ruihao-yan/ControllerFlow

## 阶段完成情况

| 阶段 | 内容 | 状态 |
|------|------|------|
| 1 | 工程骨架与核心边界（WPF App / Core / Windows / Tests，领域模型、端口、路由） | ✅ |
| 2 | 手柄输入层（Windows.Gaming.Input、按下/释放/长按、死区与抖动处理） | ✅ |
| 3 | 前台应用与 Router（Win32 前台窗口、正则匹配、前台切换 Profile） | ✅ |
| 4 | 用户映射与输出层（JSON 读写校验、键盘/鼠标/媒体/程序输出、按下释放配对） | ✅ |
| 5 | 语音方案（按住调用本地语音工具，松开结束） | ✅ |
| 6 | 震动反馈（成功/无匹配/失败三类，Binding 可覆盖强度时长） | ✅ |
| 7 | 桌面体验（Profile 编辑、按键捕获、托盘、开机自启、自检、日志） | ✅ |
| 8 | 交付（单元测试、发布脚本 scripts/publish-win-x64.ps1、README） | ✅ |

## 实现记录

- 2026-09-04：pi agent（opencode-go/deepseek-v4-flash，thinking max）依据 docs/PLAN.md 在 zip 骨架基础上完成全部阶段实现。
- 2026-09-04：Core.Tests 139 个、Windows.Tests 6 个全部通过。
- 2026-09-04：git 初始化并本地提交 1e00cb3。
- 2026-09-04：创建公开仓库并推送。

## 结构说明

- `src/ControllerFlow.Core`：领域模型、端口、路由、引擎，不依赖 Windows API
- `src/ControllerFlow.Windows`：Windows 能力封装（手柄、Win32、震动、语音、托盘）
- `src/ControllerFlow.App`：WPF 桌面应用
- `tests/`：单元测试（Core / Windows）

## 开发约定

见 AGENTS.md：禁止 icons、描述简洁、避免多余抽象。

## 后续工作

- Windows 真机验证（手柄热插拔、前台切换、输出执行、震动）
- 安装包产出与发布