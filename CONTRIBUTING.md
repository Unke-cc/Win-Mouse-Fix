# Contributing to Win Mouse Fix / Win Mouse Fix 贡献指南

本文档提供中文和英文版本。如两种文本存在不一致，以中文版本为准。

This document is provided in Chinese and English. If the two versions differ, the Chinese version prevails.

## 中文

感谢参与 Win Mouse Fix。提交前请先阅读 [文档目录](./docs/README.md) 和 [CLA.md](./CLA.md)。

### 开发要求

- 改动保持单一目的，不加入与当前需求无关的功能。
- GUI 使用 .NET Framework 4.8 + WPF，鼠标核心使用 AutoHotkey v2。
- 功能、配置或界面变化必须同步更新 README、`docs/` 或手动检查清单。
- 只提交自己编写或已取得明确授权的内容；新增第三方组件时更新 [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md)。

### 提交前检查

```powershell
.\scripts\validate.ps1
```

涉及安装程序时，再运行：

```powershell
.\scripts\package.ps1
```

Pull Request 应说明改动目的、验证结果和仍需人工检查的内容。界面变化应附截图；行为变化应更新 `tests/manual/mvp-checklist.md`。

### CLA

首次贡献时，请在 Pull Request 中留言：

> I, [legal name], have read and agree to CLA.md.

未接受 CLA 的贡献不会合并。贡献者保留自己贡献的版权，同时授予项目所有者维护、发布、收费和重新许可所需的权利。

## English

Thank you for contributing to Win Mouse Fix. Before submitting a contribution, read the [documentation index](./docs/README.md) and [CLA.md](./CLA.md).

### Development requirements

- Keep each change focused on one purpose and exclude unrelated features.
- Use .NET Framework 4.8 and WPF for the GUI, and AutoHotkey v2 for the mouse core.
- Update the README, `docs/`, or the manual checklist when behavior, configuration, or the interface changes.
- Submit only work you created or are clearly authorized to provide. Update [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md) when adding a third-party component.

### Checks before submission

```powershell
.\scripts\validate.ps1
```

For installer-related changes, also run:

```powershell
.\scripts\package.ps1
```

Each Pull Request must describe its purpose, verification results, and any remaining manual checks. Include screenshots for interface changes and update `tests/manual/mvp-checklist.md` for behavior changes.

### CLA

For your first contribution, post the following statement in the Pull Request:

> I, [legal name], have read and agree to CLA.md.

Contributions are not merged until the CLA is accepted. Contributors retain copyright in their contributions while granting the project owner the rights needed to maintain, publish, charge for, and relicense them.
