# Win Mouse Fix

Win Mouse Fix 是一款面向 Windows 的可视化鼠标快捷配置工具。通过清晰的界面配置鼠标按键、滚轮、按住滚动、按住拖动和应用规则，让鼠标操作按照用户自己的习惯工作。

> [!NOTE]
> 当前版本为 `0.1.2 Beta`，已经可以运行并生成安装程序。欢迎在不同鼠标设备和 Windows 10/11 环境中试用并提交反馈。

## 项目目标

- 使用直观的开关、选项、滑块和表单配置鼠标行为。
- 修改设置后自动保存并尽快生效，通常无需手动重启。
- 根据当前应用暂停部分或全部增强功能，离开后自动恢复。
- 常驻 Windows 系统托盘，并清楚显示启用、暂停和重新载入等状态。
- 设置保存后自动生效，减少重复操作。

## 核心功能

- 自定义中键、侧键及更多鼠标按键。
- 支持点击、双击、长按、按住滚动和按住拖动。
- 支持上下滚动、水平滚动、快速滚动、精确滚动和缩放。
- 提供开始菜单、任务视图、虚拟桌面、音量和媒体控制等常用动作。
- 支持多套配置切换、导入、导出、备份和恢复。
- 支持按应用暂停、全屏暂停、系统托盘和开机启动。
- 更新检查优先使用 GitHub，连接失败时自动使用 Gitee。
- 支持正式版和 Beta 版更新。

当前暂不支持云端同步、用户账号、在线配置分享、企业集中管理和多设备独立配置。

## 项目组成

| 部分 | 内容 | 用途 |
| --- | --- | --- |
| 可视化界面 | WPF + .NET Framework 4.8 | 设置窗口、系统托盘、状态提示和进程管理 |
| 页面状态 | WPF 数据绑定 | 页面状态和界面更新 |
| 配置 | JSON | 读取、检查和写入配置 |
| 鼠标功能核心 | 独立后台组件 | 处理按键、滚轮、组合动作和应用规则 |
| 程序打包 | Inno Setup | 生成安装版和便携版 |

## 项目结构

```text
src/
  gui/          # .NET Framework 4.8 + WPF 界面
  engine/       # 鼠标功能核心
  shared/       # 配置格式和消息格式
config/         # 默认配置
assets/         # 图标和其他资源
scripts/        # 开发、检查和构建脚本
tests/manual/   # 手动检查清单
dist/           # 安装版和便携版输出
docs/           # 产品与技术文档
```

## 本地运行与检查

开发环境需要 .NET SDK、.NET Framework 4.8 Developer Pack 和 Windows PowerShell。进入仓库目录后运行：

```powershell
.\scripts\dev.ps1
.\scripts\validate.ps1
.\scripts\build.ps1
```

`dev.ps1` 启动界面和鼠标功能核心；`validate.ps1` 检查配置、核心源文件并构建 WPF；`build.ps1` 生成 `dist/app/WinMouseFix.exe` 和 `dist/app/WinMouseFix.Engine.exe`。

当前版本已包含通用、按钮、滚动和关于四个页面，支持完整动作列表、滚动与拖动专用融合动作、Windows 常用动作、应用暂停、全屏暂停、系统托盘、开机启动、单实例和配置自动保存。首次启动会根据鼠标按键数量应用对应默认值；通用页提供配置管理，支持多套配置切换、导入、导出、备份和恢复；更新检查支持正式版和 Beta 版，并在 GitHub 不可用时改查 Gitee。

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
- [发布与安装](./docs/release-guide.md)：安装程序生成、安装、更新、卸载和发布检查。

## 后续方向

1. 支持更多鼠标按键。
2. 支持多设备独立配置。
3. 持续优化滚动体验和不同设备的兼容性。
4. 完善安装、更新和配置迁移流程。

## 参与项目

提交改动前，请阅读 [贡献指南](./CONTRIBUTING.md)、[CLA](./CLA.md) 和 [文档目录](./docs/README.md)。功能、界面或配置发生变化时，应同时更新对应文档；首次提交 Pull Request 时需要按贡献指南接受 CLA。

## License

本仓库源码公开可见，但不使用 OSI 批准的 open-source license。Win Mouse Fix `0.1.2 Beta` 允许个人非商业使用、学习和私人修改；商业使用需要胡文凯事先书面许可。

未经书面许可，不得转售或重新发布官方安装程序，不得将修改版本标示为官方版本，也不得使用 Win Mouse Fix 名称、图标或其他品牌元素推广其他产品。未来版本可以采用不同的个人收费或商业条款。

The source code is publicly visible but is not offered under an OSI-approved open-source license. Win Mouse Fix `0.1.2 Beta` permits personal, non-commercial use, study, and private modification. Commercial use requires prior written permission from Hu Wenkai. See the bilingual license files below; if the Chinese and English texts differ in interpretation, the Chinese text controls.

- [Win Mouse Fix Source-Available License](./LICENSE.md)
- [商业许可说明](./COMMERCIAL-LICENSE.md)
- [第三方组件声明](./THIRD_PARTY_NOTICES.md)
