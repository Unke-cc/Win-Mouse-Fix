# Win Mouse Fix

Win Mouse Fix 是一款面向 Windows 的鼠标快捷配置工具。项目参考 MacMouseFix 的交互方式，计划通过可视化界面配置鼠标按键、滚轮、组合动作和应用规则，让用户不必编写 AutoHotkey 代码。

> [!IMPORTANT]
> 项目已经进入 MVP 验证阶段。当前版本可以运行并生成安装程序，但公开发布前仍需完成不同鼠标、全屏游戏以及 Windows 10/11 安装环境中的实际检查。

## 项目目标

- 使用直观的开关、选项、滑块和表单配置鼠标行为。
- 修改设置后自动保存并尽快生效，通常无需手动重启。
- 根据当前应用暂停部分或全部增强功能，离开后自动恢复。
- 常驻 Windows 系统托盘，并清楚显示启用、暂停和重新载入等状态。
- AutoHotkey v2 随鼠标核心打包；GUI 使用 Windows 中的 .NET Framework 4.8。

## 首版规划

- 设置鼠标侧键和中键的动作。
- 设置滚轮方向、速度、平滑度，以及水平、快速、精确和缩放四种修饰键滚轮动作。
- 提供返回、前进、切换窗口、关闭标签页等常用动作。
- 提供总开关和系统托盘控制。
- 按应用设置停用规则。
- 自动保存配置，并在必要时只重新启动鼠标功能部分。
- 支持开机静默运行。
- 提供安装版和便携版。

首版暂不包含云端同步、用户账号、在线配置分享、企业集中管理、任意 AutoHotkey 代码编辑、针对每种鼠标驱动的单独适配，以及对 MacMouseFix 动画和滚动手感的完整还原。

## 技术方案

| 部分 | 计划采用 | 用途 |
| --- | --- | --- |
| 可视化界面 | .NET Framework 4.8 + WPF | 设置窗口、系统托盘、状态提示和进程管理 |
| 页面状态 | WPF 数据绑定 | 页面状态和界面更新 |
| 配置 | System.Text.Json | 读取、检查和写入 JSON 配置 |
| 鼠标功能 | AutoHotkey v2 | 按键、滚轮、组合动作和应用规则 |
| 设置生效 | JSON 文件自动刷新 | GUI 保存配置后，AutoHotkey v2 核心自动读取 |
| 程序打包 | Ahk2Exe + Inno Setup | 生成内部程序、安装版和便携版 |

## 项目结构

```text
src/
  gui/          # .NET Framework 4.8 + WPF 界面
  engine/       # AutoHotkey v2 鼠标功能
  shared/       # 配置格式和消息格式
config/         # 默认配置
assets/         # 图标和其他资源
scripts/        # 开发、检查和构建脚本
tests/manual/   # 手动检查清单
dist/           # 安装版和便携版输出
docs/           # 产品与技术文档
```

## 本地运行与检查

开发环境需要 .NET SDK、.NET Framework 4.8 Developer Pack、AutoHotkey v2 和 Windows PowerShell。进入仓库目录后运行：

```powershell
.\scripts\dev.ps1
.\scripts\validate.ps1
.\scripts\build.ps1
```

`dev.ps1` 启动 GUI 和鼠标核心；`validate.ps1` 检查 JSON、AutoHotkey 源文件并构建 WPF；`build.ps1` 生成 `dist/app/WinMouseFix.exe` 和 `dist/app/WinMouseFix.Engine.exe`。打包需要 Ahk2Exe，可通过 `AHK2EXE_EXE` 指定其路径，也可放在 `.tools/Ahk2Exe/Ahk2Exe.exe`。

当前 MVP 已包含通用、按钮、滚动和关于四个页面，中键与按键 4/5 的点击、双击、长按、按住并滚动和按住并拖动，以及普通操作完整动作列表、滚动与拖动专用融合动作列表、Windows 常用动作、滚轮设置、全屏暂停、应用排除、系统托盘、开机静默运行、单实例和配置自动保存。首次启动会识别三键或五键以上鼠标并应用对应默认值；按住并滚动通过一个融合动作处理上下方向，按住并拖动通过一个融合动作处理上下左右方向；快速滚动已移到滚动页，与水平滚动、精确滚动和缩放一起由键盘修饰键配合滚轮执行。通用页可打开配置管理，支持多套配置切换、导入、导出、备份和恢复；更新检查支持正式版/Beta 版，并在 GitHub 不可用时改查 Gitee。Named Pipes、更多鼠标按键和完整的平滑滚动体验尚未完成。

## 生成安装程序

安装 Inno Setup 6 后运行：

```powershell
.\scripts\validate.ps1
.\scripts\package.ps1
```

`package.ps1` 会重新生成 `dist/app`，再输出 `dist/WinMouseFix-Setup-0.1.2-beta.exe`。如无法自动找到 Inno Setup，可将 `INNO_SETUP_COMPILER` 指向 `ISCC.exe`。

安装程序按当前用户安装到 `%LocalAppData%\Programs\Win Mouse Fix`，创建开始菜单入口，并可选择创建桌面快捷方式。同一 `AppId` 的新版本会更新原安装；更新和卸载均保留 `%AppData%\WinMouseFix` 中的设置，卸载会删除 Win Mouse Fix 的登录运行登记。详细步骤见 [发布与安装](./docs/release-guide.md)。

## 系统与体积目标

- 正式支持 Windows 11 x64；兼容 Windows 10 22H2 x64。
- 暂不提供 ARM64 和 32 位版本。
- `dist/app` 中 GUI 主程序及其依赖合计不得超过 10 MiB；鼠标核心、配置和资源文件不计入该数值。
- 发布前必须运行 `validate.ps1` 和 `package.ps1`，并在上述两个 Windows 版本中完成安装、更新、卸载和启动检查。

## 文档

- [文档目录](./docs/README.md)：全部文档的分类与建议阅读顺序。
- [应用设计](./docs/application-design.md)：项目目标、功能范围、界面、配置、阶段计划和完成标准。
- [技术选择](./docs/technology-selection.md)：界面、鼠标功能、内部通信和打包方案。
- [MacMouseFix 3.0.8 功能盘点](./docs/macmousefix-feature-inventory.md)：参考产品的功能整理与 Windows 对应建议。
- [发布与安装](./docs/release-guide.md)：安装程序生成、安装、更新、卸载和发布检查。

## 计划阶段

1. 验证 WPF、AutoHotkey v2、配置保存、即时生效和组合打包。
2. 完成窗口、系统托盘、按键、滚轮、总开关和配置保存。
3. 完成应用排除、全屏暂停、活动窗口识别和状态切换。
4. 完成安装程序、便携版、开机静默运行和异常恢复。
5. 后续再评估 Named Pipes、更多鼠标按键、多设备独立配置和更完整的滚动体验。

## 参与项目

提交改动前，请阅读 [贡献指南](./CONTRIBUTING.md)、[CLA](./CLA.md) 和 [文档目录](./docs/README.md)。功能、界面或配置发生变化时，应同时更新对应文档；首次提交 Pull Request 时需要按贡献指南接受 CLA。

## License

本仓库源码公开可见，但不使用 OSI 批准的 open-source license。Win Mouse Fix `0.1.2 Beta` 允许个人非商业使用、学习和私人修改；商业使用需要胡文凯事先书面许可。

未经书面许可，不得转售或重新发布官方安装程序，不得将修改版本标示为官方版本，也不得使用 Win Mouse Fix 名称、图标或其他品牌元素推广其他产品。未来版本可以采用不同的个人收费或商业条款。

The source code is publicly visible but is not offered under an OSI-approved open-source license. Win Mouse Fix `0.1.2 Beta` permits personal, non-commercial use, study, and private modification. Commercial use requires prior written permission from Hu Wenkai. See the bilingual license files below; if the Chinese and English texts differ in interpretation, the Chinese text controls.

- [Win Mouse Fix Source-Available License](./LICENSE.md)
- [商业许可说明](./COMMERCIAL-LICENSE.md)
- [第三方组件声明](./THIRD_PARTY_NOTICES.md)
