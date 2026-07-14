# 发布与安装

本文说明 Win Mouse Fix MVP 安装程序的生成、安装、更新、卸载和发布检查。当前安装包版本为 `0.1.1`，目标系统为 Windows 11 x64，并兼容 Windows 10 22H2 x64。

## 生成安装程序

开发电脑需要 .NET SDK、.NET Framework 4.8 Developer Pack、AutoHotkey v2、Ahk2Exe 和 Inno Setup 6。Ahk2Exe 可通过 `AHK2EXE_EXE` 指定；Inno Setup 编译器可通过 `INNO_SETUP_COMPILER` 指向 `ISCC.exe`。

```powershell
.\scripts\validate.ps1
.\scripts\package.ps1
```

成功后得到：

- `dist/app/`：便携运行目录。
- `dist/WinMouseFix-Setup-0.1.1.exe`：当前用户安装程序。

`package.ps1` 会先重新生成 `dist/app`。构建期间会检查 GUI 主程序及其依赖不超过 10 MiB；鼠标核心、`config/` 和 `assets/` 不计入该数值。

## 安装行为

- 安装前检查 .NET Framework 4.8。
- 默认安装到 `%LocalAppData%\Programs\Win Mouse Fix`，用户可在安装时浏览并修改目录；安装程序不要求管理员权限，因此应选择当前用户有写入权限的位置。
- 自动创建开始菜单入口，桌面快捷方式由用户选择。
- 安装完成后可直接启动 Win Mouse Fix。
- AutoHotkey v2 已包含在鼠标核心中，用户无需单独安装。

## 更新与卸载

安装器使用固定 `AppId`。发布更高版本时运行新的安装程序，即可更新同一安装位置；安装前会关闭正在运行的 GUI 和鼠标核心。

用户设置保存在 `%AppData%\WinMouseFix`，安装、更新和卸载都不会删除该目录。卸载会移除程序文件、开始菜单入口和 `WinMouseFix` 登录运行登记。

发布新版本时，需要同时修改 `installer/WinMouseFix.iss` 中的版本号，以及 `scripts/package.ps1` 中的预期输出文件名。

## GitHub Releases 更新检查

“关于”页的“检查更新”直接读取 `Unke-cc/Win-Mouse-Fix` 的最新 GitHub Release，不需要自建服务器。发布时使用 `v0.1.1` 这类可被 `Version` 识别的 tag，并上传对应安装程序；发现更高版本后，应用会询问用户是否打开该 Release 的下载页面。

当前功能只检查和打开下载页面，不会在后台安装新版本。

## 发布检查

发布前完整执行 [MVP 手动检查清单](../tests/manual/mvp-checklist.md)，至少确认：

1. Windows 10 22H2 x64 和 Windows 11 x64 均可安装和启动。
2. 普通重复启动只显示已有窗口，`--background` 只进入托盘。
3. 开始菜单入口可用，直接更新后设置保持不变。
4. 卸载后登录运行登记已删除，用户设置仍然保留。
5. `validate.ps1`、`package.ps1` 均返回 `0`。
6. “关于”页能显示作者与许可状态，并能正确处理无 Release、已是最新版和发现新版三种结果。
