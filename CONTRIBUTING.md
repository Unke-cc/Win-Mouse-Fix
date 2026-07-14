# Contributing to Win Mouse Fix

感谢参与 Win Mouse Fix。提交前请先阅读 [文档目录](./docs/README.md) 和 [CLA.md](./CLA.md)。

## 开发要求

- 改动保持单一目的，不加入与当前需求无关的功能。
- GUI 使用 .NET Framework 4.8 + WPF，鼠标核心使用 AutoHotkey v2。
- 功能、配置或界面变化必须同步更新 README、`docs/` 或手动检查清单。
- 只提交自己编写或已取得明确授权的内容；新增第三方组件时更新 [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md)。

## 提交前检查

```powershell
.\scripts\validate.ps1
```

涉及安装程序时，再运行：

```powershell
.\scripts\package.ps1
```

Pull Request 应说明改动目的、验证结果和仍需人工检查的内容。界面变化应附截图；行为变化应更新 `tests/manual/mvp-checklist.md`。

## CLA

首次贡献时，请在 Pull Request 中留言：

> I, [legal name], have read and agree to CLA.md.

未接受 CLA 的贡献不会合并。贡献者保留自己贡献的版权，同时授予项目所有者维护、发布、收费和重新许可所需的权利。
