# Win Mouse Fix 技术选择

适用版本：`0.1.7`。本文记录当前实际采用的组成和仍未实现的方向。

## 1. 已确定的技术方案

Win Mouse Fix 只面向 Windows。鼠标核心固定为 **AutoHotkey v2**，设置窗口为 **.NET Framework 4.8 + WPF**，托盘为独立的 **.NET Framework 4.8 + Windows Forms** 进程。

| 部分 | 技术 | 用途 |
| --- | --- | --- |
| 设置窗口 | .NET Framework 4.8 + WPF | 四个设置页、HUD、配置管理 |
| 常驻控制 | WinForms `NotifyIcon` | 托盘、核心进程、登录后 `--background` |
| 页面状态 | WPF 数据绑定 | 界面更新 |
| 配置 | System.Text.Json | `%AppData%\WinMouseFix\config.json` |
| 设置生效 | JSON 文件监听 | GUI 保存后核心 250 ms 内检查 |
| 配置管理 | ProfileService + JSON | 多套配置、导入导出、备份恢复 |
| 更新检查 | HttpClient | GitHub 优先，失败改查 Gitee |
| 内存 | `MemoryService.TrimWorkingSet()` | 隐藏设置窗口或进托盘时修剪工作集 |
| 后续 IPC | NamedPipeClientStream | 未实现 |
| 鼠标核心 | AutoHotkey v2 | 手势、滚轮模式、应用规则 |
| 核心打包 | Ahk2Exe | 脚本与运行环境一并打包 |
| 安装 | Inno Setup | 当前用户安装、快捷方式、卸载 |
| 单实例 | `Mutex` + `EventWaitHandle` | 阻止重复窗口和核心 |

## 2. GUI 方案比较

| 方案 | 优点 | 不足 | 结论 |
| --- | --- | --- | --- |
| .NET Framework 4.8 + WPF | Win10 22H2 / Win11 已带运行库，体积小 | 默认样式传统，需自绘 | 采用 |
| WinUI 3 | 更接近 Windows 11 | 托盘与打包更复杂 | 不采用 |
| Tauri 2 / Electron | 页面样式自由 | 体积和组成更重 | 不采用 |

## 3. GUI 与鼠标核心的关系

GUI 是配置的唯一写入者。核心只读取并执行，不改用户设置。登录运行命令是 `WinMouseFix.Tray.exe --background`。

滚轮：`takeOverWheel=false` 时不注册全局滚轮热键，仅在已映射的中键/侧键按下时临时挂滚轮，用于按钮页手势。平滑/惯性才常驻接管垂直滚轮速度。核心不再用键盘修饰键拦截普通滚动，也不再按滚轮频率猜测触控板。

Named Pipes 未加入。滑块使用 GUI 现有延迟保存。无效配置时核心沿用上一份有效配置。

## 4. 系统与发布体积

- Windows 11 x64 正式支持，Windows 10 22H2 x64 兼容；无 ARM64 / 32 位。
- 安装检查 .NET Framework 4.8；AHK 打进核心。
- 默认 `%LocalAppData%\Programs\Win Mouse Fix`，不要求管理员。
- 固定 `AppId`；卸载删登录运行登记，保留用户配置目录。
- `dist/app` 中 GUI+托盘主程序及依赖不超过 10 MiB；核心和 `config/` 不计。
- 不使用 self-contained、PublishTrimmed、NativeAOT、ReadyToRun。

## 5. 最小验证清单

- 改一侧键动作后，JSON 写入、核心读取、行为变化。
- 原生 / 平滑 / 惯性三模式切换后手感符合选择；原生下触控板不被加速或截流。
- 按住侧键滚动执行按钮页手势，不经过惯性尾巴。
- 左键、右键无法录入手势。
- 无映射键按下时位移定时器不空转。
- GitHub 失败改查 Gitee。
- 无效配置时核心不退出。
- 重复普通启动只前置窗口；重复 `--background` 不弹窗。
- `validate.ps1`、`package.ps1` 成功；GUI+托盘体积不超过 10 MiB。
