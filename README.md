# Win Mouse Fix

Win Mouse Fix 是面向 Windows 的鼠标辅助工具。通过设置窗口配置中键、侧键手势和滚轮手感，让鼠标按自己的习惯工作。

> [!NOTE]
> 当前版本为 `0.1.7`。安装程序为 `dist/WinMouseFix-Setup-0.1.7.exe`。欢迎在不同鼠标和 Windows 10/11 上试用并反馈。

## 项目目标

- 用开关、选项、滑块和表单配置鼠标行为。
- 修改后自动保存并尽快生效，通常不必手动重启。
- 排除应用和全屏时暂停增强功能，离开后恢复。
- 常驻系统托盘，设置窗口按需打开。

## 0.1.7 核心功能

- 自定义中键、按键 4、按键 5；支持点击、双击、长按、按住滚动、按住拖动。
- 左键、右键不能作为手势键，保持系统原始行为。
- 滚轮三种模式：Windows 原生、平滑流动、惯性飞轮。原生模式不接管滚轮速度。
- 按住中键或侧键时的滚轮手势在按钮页配置，不使用键盘修饰键拦截滚轮。
- 应用排除、全屏自动暂停、登录后托盘运行（后两项固定开启，界面无开关）。
- 多套配置切换、导入、导出、备份和恢复。
- 更新检查优先 GitHub，失败后改查 Gitee；支持正式版和 Beta。

当前不支持：把触控板当主输入、按设备来源精确区分触控板与鼠标、云端同步、账号、在线配置分享、企业集中管理、多设备独立配置。

## 项目组成

| 部分 | 内容 | 用途 |
| --- | --- | --- |
| 设置界面 | WPF + .NET Framework 4.8 | 四个设置页、状态提示、配置写入 |
| 托盘 | Windows Forms `NotifyIcon` | 常驻、启动设置窗口、管理核心进程 |
| 配置 | JSON | `%AppData%\WinMouseFix\config.json` |
| 鼠标核心 | AutoHotkey v2 | 按键、滚轮、应用排除、全屏暂停 |
| 程序打包 | Inno Setup | 安装版；`dist/app` 为便携目录 |

## 项目结构

```text
src/
  gui/          # 设置窗口
  tray/         # 托盘进程
  engine/       # 鼠标核心
  shared/       # 配置格式和消息格式
config/         # 默认配置
assets/         # 图标和其他资源
scripts/        # 开发、检查和构建脚本
tests/manual/   # 手动检查清单
dist/           # 安装版和便携版输出
docs/           # 产品与技术文档
```

## 本地运行与检查

开发环境需要 .NET SDK、.NET Framework 4.8 Developer Pack 和 Windows PowerShell：

```powershell
.\scripts\dev.ps1
.\scripts\validate.ps1
.\scripts\build.ps1
```

`dev.ps1` 启动设置窗口、托盘和鼠标核心；`validate.ps1` 检查配置、核心源文件并构建 WPF；`build.ps1` 生成 `dist/app/WinMouseFix.exe`、`dist/app/WinMouseFix.Tray.exe` 和 `dist/app/WinMouseFix.Engine.exe`。

首次启动按鼠标按键数量套用默认映射。设置窗口宽 480 px，内容区 440 px，切换页面时左上角固定、只改高度。通用、滚动、关于页没有整页滚动条。

## 生成安装程序

安装 Inno Setup 6 后运行：

```powershell
.\scripts\validate.ps1
.\scripts\package.ps1
```

输出 `dist/WinMouseFix-Setup-0.1.7.exe`。找不到 Inno Setup 时，把 `INNO_SETUP_COMPILER` 指向 `ISCC.exe`。

安装到 `%LocalAppData%\Programs\Win Mouse Fix`（当前用户，可不选管理员）。更新和卸载保留 `%AppData%\WinMouseFix`；卸载删除登录运行登记。登录运行命令为 `WinMouseFix.Tray.exe --background`。步骤见 [发布与安装](./docs/release-guide.md)。

## 系统与体积

- 正式支持 Windows 11 x64；兼容 Windows 10 22H2 x64。
- 暂不提供 ARM64 和 32 位版本。
- `dist/app` 中 GUI 与托盘主程序及其依赖合计不得超过 10 MiB；鼠标核心、配置不计入该数值。
- 发布前必须运行 `validate.ps1` 和 `package.ps1`。

## 文档

- [文档目录](./docs/README.md)
- [应用设计](./docs/application-design.md)
- [技术选择](./docs/technology-selection.md)
- [发布与安装](./docs/release-guide.md)
- [0.1.7 发布说明](./docs/v0.1.7-release-notes.md)

## 后续方向

1. 支持更多鼠标按键。
2. 评估更精确的输入设备来源识别。
3. 长映射列表在矮屏幕上的内部滚动。
4. 多设备独立配置。

## 参与项目

提交前阅读 [贡献指南](./CONTRIBUTING.md)、[CLA](./CLA.md) 和 [文档目录](./docs/README.md)。功能、界面或配置变化时同步改文档。首次 Pull Request 需按贡献指南接受 CLA。

## License

本仓库源码公开可见，但不使用 OSI 批准的 open-source license。Win Mouse Fix `0.1.7` 允许个人非商业使用、学习和私人修改；商业使用需要胡文凯事先书面许可。详见 [LICENSE.md](./LICENSE.md)、[COMMERCIAL-LICENSE.md](./COMMERCIAL-LICENSE.md)、[THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md)。
