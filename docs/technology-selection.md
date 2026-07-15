# Win Mouse Fix 技术选择

## 1. 已确定的技术方案

Win Mouse Fix 只面向 Windows，以 **AutoHotkey v2** 作为固定的鼠标核心，以 **.NET Framework 4.8 + WPF** 构建 GUI。GUI 将复杂的鼠标脚本能力转换为开关、选项、滑块和表单；AutoHotkey v2 负责按键、滚轮、组合动作、活动应用识别及应用规则。

| 部分 | 技术 | 用途 |
| --- | --- | --- |
| GUI | .NET Framework 4.8 + WPF | 设置窗口、托盘、状态提示和核心进程管理 |
| 页面状态 | WPF 数据绑定 | 页面状态和界面更新 |
| 配置 | System.Text.Json | 读取、检查和写入 JSON 配置 |
| MVP 设置生效 | JSON 文件监听 | GUI 保存配置后，核心在 250 毫秒内检查并读取 |
| 后续进程通信 | NamedPipeClientStream | 查询状态、确认结果和发送控制命令 |
| 鼠标核心 | AutoHotkey v2 | 执行全部鼠标行为和应用规则 |
| 核心打包 | Ahk2Exe | 将脚本与 AutoHotkey v2 运行环境打包 |
| 整体安装 | Inno Setup | 生成安装版，处理快捷方式、开机运行和卸载 |
| 单实例 | `Mutex` + `EventWaitHandle` | 阻止重复窗口和核心，并让普通重复启动显示已有窗口 |

## 2. GUI 方案比较

| 方案 | 优点 | 不足 | 结论 |
| --- | --- | --- | --- |
| .NET Framework 4.8 + WPF | Windows 10 22H2 和 Windows 11 已包含所需框架，发布文件较小；便于管理托盘、进程和安装包 | 默认样式较传统，需要自定义控件外观 | 采用 |
| WinUI 3 | Windows 11 风格自然 | 托盘、打包和部分窗口行为更复杂 | 暂不采用 |
| Tauri 2 | 页面样式和动画自由 | 同时引入 Web、Rust 和 WebView2，组成更复杂 | 暂不采用 |
| Electron | 页面开发方便，相关资料丰富 | 安装体积和运行占用较大 | 不采用 |

## 3. GUI 与鼠标核心的关系

GUI 是配置的唯一写入者，用户配置保存在 `%AppData%\WinMouseFix\config.json`。AutoHotkey v2 只读取、检查并执行配置，不直接改写用户设置。

GUI 使用当前用户会话中的 `Mutex` 保证单实例，并用 `EventWaitHandle` 通知已有实例显示设置窗口。`--background` 用于登录后静默进入托盘；重复的后台启动直接结束，不主动显示窗口。

MVP 中，GUI 保存 JSON 配置，AutoHotkey v2 核心监听文件变化并自动读取。这个方式已经能够让普通设置生效，但无法提供完整的状态确认。

GUI 在 `%AppData%\WinMouseFix` 中管理当前配置、多套配置档案和备份。切换、导入或恢复后仍写入固定的 `config.json`，因此 AutoHotkey v2 核心不需要理解配置档案名称。

后续版本再加入 Windows Named Pipes。消息至少支持应用配置、暂停、恢复、读取状态和重新启动核心；AutoHotkey v2 返回请求编号、配置版本、执行结果和错误信息，GUI 只在收到成功确认后显示“已生效”。

普通选项应即时生效；滑块和连续修改在用户停止操作约 400 毫秒后保存，减少重复写入。新配置无法使用时，核心继续使用上一份有效配置并返回错误；只有无法直接更新的设置才重新启动 AutoHotkey v2 核心，GUI 始终保持打开。

## 4. 系统与发布体积

- 正式支持 Windows 11 x64，兼容 Windows 10 22H2 x64；暂不提供 ARM64 和 32 位版本。
- 安装程序检查 .NET Framework 4.8，AutoHotkey v2 随鼠标核心打包。
- 安装到 `%LocalAppData%\Programs\Win Mouse Fix`，不要求管理员权限；开始菜单入口默认创建，桌面快捷方式由用户选择。
- 固定 `AppId` 支持直接更新；安装和卸载保留 `%AppData%\WinMouseFix`，卸载删除登录运行登记。
- `dist/app` 中 GUI 主程序及其依赖合计不得超过 10 MiB；`WinMouseFix.Engine.exe` 和 `config/` 不计入 GUI 数值。界面图标已嵌入 GUI，不在发布目录重复保存。
- 不使用 self-contained、PublishTrimmed、NativeAOT 或 ReadyToRun；这些方式不适合当前 WPF 目标，或会明显增加发布体积。

## 5. 最小验证清单

- 运行 WPF GUI 与打包后的 AutoHotkey v2 核心，确认核心能够读取 GUI 保存的配置。
- 在 GUI 中修改一个侧键动作，确认配置写入、核心自动读取和鼠标行为即时变化。
- 修改滚轮参数并连续拖动滑块，确认最终数值生效且没有明显卡顿。
- 写入无效配置，确认核心回到默认配置且保持运行；完整的错误提示待 Named Pipes 完成后验证。
- 修改必须重新初始化的设置，确认只重启鼠标核心，GUI 页面、窗口位置和未保存表单不丢失。
- 关闭设置窗口后确认核心继续运行；重新打开 GUI 后确认状态和配置一致。
- 连续普通启动两次，确认第二次结束且已有窗口显示到前台；重复 `--background` 启动不显示窗口。
- 使用 Ahk2Exe 与 Inno Setup 生成安装包，在未单独安装 AutoHotkey 的 Windows 环境中验证安装、运行和卸载。
- 分别在 Windows 10 22H2 x64 和 Windows 11 x64 检查启动、托盘、配置保存和卸载。
- 检查开始菜单入口、直接更新、卸载后配置保留以及登录运行登记删除。
- 运行构建脚本后，确认其报告的 GUI 总量不超过 10 MiB。
