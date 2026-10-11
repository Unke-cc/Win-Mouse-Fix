# Win Mouse Fix 文档目录

`docs/` 保存产品、设计、发布文档。新增文档必须在这里登记。

## 阅读顺序

1. [应用设计文档](./application-design.md)：`0.1.7` 功能、页面、配置和验收。
2. [技术选择](./technology-selection.md)：程序组成和未实现方向。
3. [发布与安装](./release-guide.md)：打包、安装、更新、卸载。
4. [0.1.7 发布说明](./v0.1.7-release-notes.md)：本版本相对用户可见的变化。
5. [0.1.7 发布验收清单](../tests/manual/v0.1.7-release-checklist.md) 与 [发布候选检查清单](../tests/manual/mvp-checklist.md)。
6. [项目许可](../LICENSE.md)
7. [贡献指南](../CONTRIBUTING.md)

`tests/manual/v0.1.2-beta-update-checklist.md` 和 `v0.1.3-beta-update-checklist.md` 只记录当时版本，不代表 `0.1.7`。

## 文档分类

| 分类 | 用途 | 当前文档 |
| --- | --- | --- |
| 产品与设计 | 功能边界和界面 | [应用设计](./application-design.md)、[MacMouseFix 对照](./macmousefix-feature-inventory.md) |
| 开发 | 技术选择 | [技术选择](./technology-selection.md) |
| 测试 | 手工检查 | [mvp-checklist](../tests/manual/mvp-checklist.md)、[v0.1.7-release-checklist](../tests/manual/v0.1.7-release-checklist.md) |
| 发布 | 打包安装 | [发布与安装](./release-guide.md)、[0.1.7 发布说明](./v0.1.7-release-notes.md) |
| 用户帮助 | 面向普通用户的 FAQ | 暂无 |
| 许可与贡献 | 使用范围和贡献授权 | [LICENSE](../LICENSE.md)、[商业许可](../COMMERCIAL-LICENSE.md)、[第三方](../THIRD_PARTY_NOTICES.md)、[CONTRIBUTING](../CONTRIBUTING.md)、[CLA](../CLA.md) |

## 维护规则

- 文件名小写英文加连字符。
- 每份文档只负责一个主题。
- 功能、界面或配置变化时，同一次提交更新对应文档。
- 命令、路径、配置示例必须能实际执行或读取。
- 未完成的内容标“待定”，不得写成已完成。
- `macmousefix-feature-inventory.md` 是对照 Mac Mouse Fix 3.0.8 的映射表，以 Win Mouse Fix 列的实现为准。

仓库根目录 `AGENTS.md` 是贡献者工作规则，不属于本手册正文。
