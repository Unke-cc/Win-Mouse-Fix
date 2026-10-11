# 发布与安装

本文说明 Win Mouse Fix `0.1.7` 安装程序的生成、安装、更新、卸载和发布检查。目标系统为 Windows 11 x64，兼容 Windows 10 22H2 x64。

## 生成安装程序

需要 .NET SDK、.NET Framework 4.8 Developer Pack、AutoHotkey v2、Ahk2Exe、Inno Setup 6。可用 `AHK2EXE_EXE`、`INNO_SETUP_COMPILER` 指定路径。

```powershell
.\scripts\validate.ps1
.\scripts\package.ps1
```

成功后得到：

- `dist/app/`：便携运行目录。
- `dist/WinMouseFix-Setup-0.1.7.exe`：当前用户安装程序。

发布时记录安装包大小和 SHA256，以最后一次打包输出为准。

`package.ps1` 先生成 `dist/app`。GUI 与托盘主程序及其依赖不超过 10 MiB；鼠标核心和 `config/` 不计。托盘图标在 `dist/app/assets/`。

## 安装行为

- 安装前检查 .NET Framework 4.8。
- 默认 `%LocalAppData%\Programs\Win Mouse Fix`，不要求管理员。
- 创建开始菜单入口；桌面快捷方式可选。
- AutoHotkey v2 已打进鼠标核心。

## 更新与卸载

固定 `AppId`。新安装包更新同一位置；安装前关闭 GUI、托盘和核心。

用户设置在 `%AppData%\WinMouseFix`，安装、更新、卸载都不删该目录。卸载移除程序文件、开始菜单入口和 `WinMouseFix` 登录运行登记。登录运行命令为 `WinMouseFix.Tray.exe --background`。

改版本号时同时改 `installer/WinMouseFix.iss` 和 `scripts/package.ps1` 中的输出文件名。未要求前不要把版本升到 `1.0`。

## GitHub 与 Gitee 更新检查

关于页默认只查正式版；勾选 Beta 后接受预发布。先读 GitHub `Unke-cc/Win-Mouse-Fix`（约 3 秒），失败改查 Gitee `unkecc/Win-Mouse-Fix`。项目主页、许可、更新页使用本次可访问的同一来源。

两边使用相同 `v0.1.7` tag，上传同一安装包和 `.sha256`。正式版不能标预发布。发现新版本后，用户确认才下载和安装。

## 发布检查

发布前执行 [发布候选检查清单](../tests/manual/mvp-checklist.md) 和 [0.1.7 发布验收清单](../tests/manual/v0.1.7-release-checklist.md)，至少确认：

1. Windows 10 22H2 x64 和 Windows 11 x64 可安装启动。
2. 重复启动只显示已有窗口；`WinMouseFix.Tray.exe --background` 只进托盘。
3. 开始菜单可用；直接更新后设置保留。
4. 卸载后登录运行登记删除，用户设置仍在。
5. `validate.ps1`、`package.ps1` 返回 0。
6. 关于页能处理无 Release、已是最新、发现新版。
7. 三模式滚动、原生下放行触控板、左键右键不可手势、HUD 胶囊已手工确认。
8. 发布记录含文件名、大小、SHA256、GitHub 与 Gitee 地址。
