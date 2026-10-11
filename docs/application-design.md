# Win Mouse Fix 应用设计文档

## 1. 文档信息

- 状态：与仓库当前实现一致
- 适用版本：0.1.7
- 用途：统一产品范围、用户操作、程序组成、发布方式和验收标准

## 2. 产品目标

Win Mouse Fix 是 Windows 鼠标辅助工具，交互参考 Mac Mouse Fix 3.0.8。用户在设置窗口里配置中键、侧键手势和滚轮手感；AutoHotkey v2 核心执行这些行为。目标用户不必编写脚本。

程序应满足：

- 用户无需单独安装 AutoHotkey；设置窗口使用系统自带的 .NET Framework 4.8。
- 设置完成后立即生效，通常不需要手动重启。
- 排除应用、全屏应用中暂停增强功能，离开后恢复。
- 设置在关闭、更新或重启后保留。
- 托盘常驻；设置窗口按需打开。
- 只保留一个设置窗口、一个托盘进程、一个鼠标核心。
- 左键、右键永不作为手势键。
- 不把触控板当主输入；未选择平滑或惯性时，不全局接管滚轮速度。

## 3. 0.1.7 已实现范围

- 中键、按键 4、按键 5 的点击、双击、长按、按住滚动、按住拖动。
- 普通动作列表，以及按住滚动 / 按住拖动专用融合动作。
- 滚轮三模式：Windows 原生（不接管速度）、平滑流动、惯性飞轮；另有反向与速度。
- 按住中键或侧键时的滚轮/拖动手势在按钮页配置；界面不再提供 Ctrl/Alt/Shift/Win 拦截滚轮。
- 三键 / 五键默认映射自动识别，以及“恢复默认值...”手动选择（无“推荐”字样）。
- 总开关、托盘、应用排除、全屏暂停、登录运行、单实例。登录运行与全屏暂停固定为开，界面无对应开关。
- 配置自动保存、多套配置、导入、导出、备份、恢复。
- GitHub 优先、Gitee 回退、正式版 / Beta 更新检查。
- 设置窗口关闭 5 分钟无操作后进入轻量模式；托盘可立即进入。关闭或最小化时修剪工作集。
- 安装版和便携版。

当前不包含：云端同步、账号、在线配置分享、企业集中管理、任意脚本编辑、多设备独立配置、按设备来源精确区分触控板和实体鼠标。

## 4. 用户使用流程

1. 安装或直接运行 Win Mouse Fix。
2. 主动启动显示设置窗口；登录后由 `WinMouseFix.Tray.exe --background` 静默进托盘。
3. 在按钮页录入手势，在滚动页选择滚轮模式。
4. 修改后自动保存并尽快生效。
5. 需要重新初始化时只重启鼠标核心，设置窗口保持打开。
6. 进入排除应用或真正全屏时暂停增强功能；离开后恢复。

## 5. 界面设计

主窗口宽 **480 px**，内容区 **440 px**，左上角固定，切换页面只对高度做约 220 ms 缓动。通用、滚动、关于页禁用整页滚动条。按钮页只允许映射列表内部滚动。状态用底部深色圆角 HUD 胶囊，不用底部蓝条。

顶部横向选择器（图标 + 标签）：

- **通用**：大标题“启动 Win Mouse Fix”总开关、配置方案管理、应用排除。无“运行偏好”分组标题，无登录/全屏开关。
- **按钮**：150 px 加号录入区、双列表格（左列 130 px，1 px 分割线贯通分组，行高 39 px，表头 24 px，无系统选中蓝）。手势键仅中键和侧键。
- **滚动**：三选一模式（原生 / 平滑 / 惯性）、反向滚动、速度滑块。
- **关于**：版本、作者、许可、项目主页、Beta 开关、GitHub/Gitee 更新检查。

辅助窗口宽 450 px：按钮选项（双击速度显示为“快 · 150 ms”）、恢复默认值、配置管理（删除为红色）。

普通选项无单独“应用”按钮。界面和托盘应能区分启用、用户暂停、应用排除暂停、重新载入、配置失败回退默认。

## 6. 技术与发布方案

### 6.1 产品结构

三进程：设置窗口 `WinMouseFix.exe`（WPF，按需）、托盘 `WinMouseFix.Tray.exe`（WinForms `NotifyIcon`，常驻）、核心 `WinMouseFix.Engine.exe`（AutoHotkey v2）。配置由 GUI 写入 JSON，核心只读。

### 6.2 AutoHotkey v2 鼠标核心

负责按键手势、滚轮模式、活动应用识别、全屏暂停。滚轮热键仅在 `takeOverWheel` 为真，或已映射按键处于按下时注册；原生模式平时完全放行系统滚轮和触控板。

惯性：同向连续至少 4 格，且最近间隔平均小于 60 ms 才注入动量；反向或间隔超过 160 ms 重置；阻尼 0.72，速度低于 0.25 停止。按住按钮的手势滚轮不走平滑/惯性。热键频率上限 2000 次 / 2 秒，并挂 `OnError`，避免快滚退出。位移检测定时器仅在已映射按键按下时以 15 ms 运行。

左键、右键不得注册为手势热键。

### 6.3 设置窗口与托盘

GUI 不实现底层鼠标钩子。单实例用会话 `Mutex`；普通重复启动用 `EventWaitHandle` 显示已有窗口。重复 `--background` 直接结束。登录运行登记为 `"WinMouseFix.Tray.exe" --background`。

便携路径下，登录运行会把托盘可执行文件的绝对路径写入注册表；移动目录后需重新运行一次程序以更新登记。

### 6.4 轻量模式

设置窗口关闭，或连续 5 分钟无键鼠操作后，设置进程保存并退出，只留托盘和核心。再次打开设置时启动 `WinMouseFix.exe`，不重复启动核心。隐藏或进入托盘时调用 `MemoryService.TrimWorkingSet()`。

### 6.5 两部分通信

GUI 延迟 400 ms 写 JSON；核心每 250 ms 检查文件变化。Named Pipes 未实现。无法读取的新配置时核心继续用上一份有效配置。

### 6.6 Inno Setup

当前用户安装，默认 `%LocalAppData%\Programs\Win Mouse Fix`。固定 `AppId`。不删除 `%AppData%\WinMouseFix`。卸载删除登录运行登记。`package.ps1` 输出 `dist/WinMouseFix-Setup-0.1.7.exe`。

### 6.7 系统与体积

正式支持 Windows 11 x64，兼容 Windows 10 22H2 x64。无 ARM64 / 32 位。GUI 与托盘主程序及其依赖在 `dist/app` 中合计不超过 10 MiB。

## 7. 程序组成

```text
src/
  gui/
  tray/
  engine/
    MouseEngine.ahk
    lib/
  shared/
    config.schema.json
    messages.json
    MemoryService.cs
config/default.json
```

- `gui/`：设置窗口。
- `tray/`：托盘与核心进程控制。
- `MouseManager`：按键与滚轮。
- `ActionManager`：动作执行。
- `AppRuleManager`：排除应用与全屏。
- `ConfigManager`：读配置。
- `shared/`：schema、消息、工作集修剪。

## 8. 配置设计

默认配置 `config/default.json`，生效配置 `%AppData%\WinMouseFix\config.json`。档案在 `profiles/`，备份在 `backups/`，当前档案名在 `active-profile.txt`。

顶层字段：`configVersion`（恒为 2）、`enabled`、`pauseInFullscreen`、`receiveBetaUpdates`、`doubleClickSpeed`、`desktopSwipeDirection`、`remaps`、`scroll`、`timing`、`excludedApps`、`startup`。`desktopSwipeDirection` 仅为旧配置兼容。

`remaps.button` 仅 `MButton`、`XButton1`、`XButton2`。触发：`click`、`doubleClick`、`hold`、`holdScroll`、`holdDrag`；旧 `wheelUp` / `wheelDown` / 方向拖动仍可读。`holdScroll`、`holdDrag` 各选一个融合动作。

滚动模式与 JSON 对应：

| 界面 | `takeOverWheel` | `smooth` | `inertia` |
| --- | --- | --- | --- |
| Windows 原生 | `false` | 忽略 | `false` |
| 平滑流动 | `true` | `true` | `false` |
| 惯性飞轮 | `true` | `false` | `true` |

仓库 `config/default.json` 当前为 `takeOverWheel: true`、`smooth: false`、`inertia: false`。界面按“非原生且非惯性”显示为平滑流动。`startup.runAtLogin` 与 `pauseInFullscreen` 启动时被强制写成 `true`。

`scroll` 仍保留 `horizontalModifier`、`fastModifier`、`precisionModifier`、`zoomModifier` 字段以兼容旧配置和 schema；0.1.7 界面不提供这些全局拦截，核心滚轮路径也不再按键盘修饰键改写普通滚动。系统自带的 Ctrl 缩放、Shift 横向滚动在原生模式下保持原样。

`timing.dragThresholdPx` 默认 12，范围 5–200。

应用排除 `mode` 仅 `disableAll`。

保存前检查类型、范围和路径；先写临时文件再替换。

## 9. 设置生效与自动重新载入

按键、滚轮、总开关、排除列表即时生效。不能热更新的项只重启核心。新配置无效时恢复上一份有效配置并提示。

## 10. 应用排除列表

可从正在运行的程序选择，或手动选 `.exe`。命中后停止全部增强动作。切入时取消未完成手势并松开程序按下的键。识别优先完整路径，否则用文件名。

## 11. 状态与异常恢复

优先级：退出或重新载入 > 用户暂停 > 排除应用 / 全屏暂停 > 正常启用。

全屏暂停识别普通窗口状态且完整覆盖显示器的窗口。普通最大化、桌面、任务栏、虚拟桌面切换界面不暂停。多显示器时可能误判，这是已知限制。

- 配置不存在：按鼠标按键数写默认配置。
- 配置无效：保留原文件副本，用默认配置并提示。
- 应用路径失效：标记不可用，不自动删除。
- 动作中断：释放已按下的键。

## 12. 当前限制

1. Windows 无法提供可靠的 `isContinuous` / 设备来源；原生模式 = 不挂全局滚轮热键。触控板 120-delta 过滤未作为已交付行为。
2. 映射列表过长时没有 MaxHeight，矮屏幕可能超出窗口。
3. 便携版登录运行登记使用托盘绝对路径。
4. 4–8 kHz 鼠标或厂商改键软件（如 G HUB）可能导致侧键丢失。
5. 尚未实现 Named Pipes 状态确认。

## 13. 验证标准

### 13.1 基础功能

- `dev.ps1` 能启动设置窗口、托盘和核心。
- 重复启动只显示已有窗口；`--background` 不弹窗。
- 登录后托盘运行，窗口不自动弹出。
- 左键、右键无法录入手势。
- 原生模式：未按映射键时触控板与系统滚轮无额外延迟。
- 平滑 / 惯性仅在选中对应模式后改变机械滚轮手感；按住侧键/中键的手势滚轮即拨即停。
- 惯性：慢速逐格停，极速连拨后有阻尼尾，不误触。
- GitHub 失败改查 Gitee；Beta 开关可发现预发布。
- 退出再开，设置保留。

### 13.2 应用排除与全屏

- 普通窗口、无边框全屏、真正全屏分别测试。
- 切入排除应用时清理按键状态。

### 13.3 配置

- 无效配置可恢复并提示。
- 旧 `configVersion: 1` 可迁到 `remaps`。
- 恢复默认值窗口无“推荐”字样。

### 13.4 打包

- `validate.ps1` 成功。
- `package.ps1` 生成 `dist/WinMouseFix-Setup-0.1.7.exe`。
- GUI+托盘依赖不超过 10 MiB。
- 开始菜单、更新、卸载、便携、登录静默运行符合预期。

手工清单见 [mvp-checklist.md](../tests/manual/mvp-checklist.md) 和 [v0.1.7-release-checklist.md](../tests/manual/v0.1.7-release-checklist.md)。

## 14. 相关文档

- [MacMouseFix 3.0.8 功能盘点](./macmousefix-feature-inventory.md)
- [技术选择](./technology-selection.md)
- [发布与安装](./release-guide.md)
- [0.1.7 发布说明](./v0.1.7-release-notes.md)
