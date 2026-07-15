# MacMouseFix 3.0.8 功能盘点

## 目的与范围

本文根据 MacMouseFix 3.0.8 源码整理其用户功能、交互方式和后台职责，用于确定 Win Mouse Fix 的产品范围。Win Mouse Fix 以 AutoHotkey v2 为鼠标功能核心，GUI 参考其使用方式，但 Windows 没有直接对应的能力时，以达到相近结果为准。

## GUI 页面

| 页面 | 功能 | Win Mouse Fix 建议 |
|---|---|---|
| General | 总开关、菜单栏常驻、更新检查、预览版本 | 对应为总开关、系统托盘、自动更新选项 |
| Buttons | 录入鼠标触发方式，为其选择动作，并以列表编辑或删除 | 作为首版主页面，保留“直接操作鼠标完成录入” |
| Scrolling | 平滑程度、触控板模拟、方向反转、速度、精确滚动和组合键模式 | 首版实现基础项目，高质量连续滚动单独验证 |
| Pointer | 灵敏度与加速度 | 源码中尚未完成且默认隐藏，Win Mouse Fix 放到后续版本 |
| About | 版本、购买、试用和许可状态 | 首版只需要版本、项目链接、更新状态和第三方许可 |

依据：`App/UI/Main/Base.lproj/Main.storyboard:213` 定义页面结构；`App/UI/Main/TabViewController.swift:174-194` 隐藏未完成的 Pointer 页面；`App/UI/Main/Tabs/GeneralTabController.swift:20-35` 定义 General 选项。

## Buttons：触发模型

用户把鼠标移入新增区域后，后台进入录入状态；用户实际完成一次鼠标操作，GUI 收到识别结果并新增一行，然后让用户选择动作。这种方式比要求用户理解“按键编号、次数、长按”等表单更直观，应作为 Win Mouse Fix 的关键交互。

触发条件由四部分组成：鼠标键、点击次数、操作类型、前置条件。支持单击、双击、三击、长按、双击后按住、三击后按住，以及对应的拖动和滚轮操作；前置条件可以是其他鼠标键序列或键盘组合键。依据：`App/UI/Main/Tabs/ButtonTab/ButtonTabController.swift:549-603`、`App/UI/Main/Tabs/ButtonTab/RemapTable/RemapTableTranslator.m:657-719`、`:741-787`。

## Buttons：动作清单

- 鼠标动作：主键、次键、中键。
- 导航动作：Back、Forward。
- Windows 对应动作：任务视图、显示桌面、显示当前应用窗口、切换左/右虚拟桌面。
- 自定义动作：录入并执行键盘快捷键。
- macOS 专属动作：Look Up、Smart Zoom、Launchpad；不直接照搬，应改为 Windows 可理解的动作或暂不提供。
- 拖动与滚轮触发：按住鼠标键并移动或滚动，用于连续滚动、窗口/桌面切换等手势。

动作定义依据：`App/UI/Main/Tabs/ButtonTab/RemapTable/RemapTableTranslator.m:137-208`。Win Mouse Fix 只提供预定义动作和安全的表单选项，不把通用 AutoHotkey 脚本编辑器暴露给普通用户。

## Scrolling 与 Pointer

Scrolling 包含平滑等级、触控板式滚动、反向、速度、慢速滚轮精确控制，以及横向、缩放、快速、精确四类组合键模式。配置入口见 `App/UI/Main/Tabs/ScrollTabController.swift:18-26`，界面绑定见 `:95-121`。

Pointer 设计了 0.5x、0.75x、1.0x、1.5x、2.0x 灵敏度刻度和加速度档位，但源码使用固定初值，且页面被隐藏，不能视为 3.0.8 的正式功能。依据：`App/UI/Main/Tabs/PointerTabController.swift:16-25`、`:35-75`。

## 常驻、设备、配置、更新与许可

- 后台组件持续接收鼠标输入，GUI 负责显示和修改配置；两者相互通知配置变化。
- General 提供总开关和菜单栏常驻；Win Mouse Fix 对应为 AutoHotkey v2 核心开关与系统托盘。
- 程序根据鼠标事件的设备来源更新当前设备，见 `App/AppDelegate.m:243-256`；Windows 多设备区分需要专门样例验证。
- 配置提交后通知后台组件，见 `App/AppDelegate.m:321-322`；Win Mouse Fix 采用 GUI 写入 JSON、再发送生效指令的方式。
- 更新由 Sparkle 处理，可选择稳定版或预览版，见 `App/AppDelegate.m:288-330`。Windows 需要采用适合安装包的独立更新方案。
- About 包含试用、购买、启用和许可状态，见 `App/UI/Main/Tabs/AboutTabController.swift:99-166`。Win Mouse Fix 是否收费是独立产品决策；无论如何都要保留 AutoHotkey 和其他第三方组件的许可说明。

## Windows 与 AutoHotkey v2 实现判断

| 能力 | 难度 | 说明 |
|---|---|---|
| 单击、多击、长按、组合键 | 低至中 | AutoHotkey v2 可完成，需统一处理时间阈值和原始按键恢复 |
| Back、Forward、鼠标键、键盘快捷键 | 低 | 有直接的 Windows 输入对应方式 |
| 任务视图、显示桌面、虚拟桌面切换 | 低至中 | 可调用 Windows 快捷键，需验证不同系统版本 |
| 按住并拖动的方向手势 | 中 | 需要方向阈值、取消条件和屏幕缩放适配 |
| 滚轮速度、反向、组合键模式 | 中 | 可实现，但需覆盖不同软件和高分辨率滚轮 |
| 平滑、惯性、二维连续滚动 | 高 | AutoHotkey v2 能模拟事件，但手感与稳定性必须通过样例确认 |
| 指针灵敏度、加速度 | 高 | 可能影响系统全局设置，不建议进入首版 |
| 区分多只鼠标 | 高且不确定 | AutoHotkey v2 常规输入不足，需要 Windows Raw Input 或额外组件 |

## 版本建议

首版包含 Buttons 录入体验、常用动作、滚轮反向与速度、总开关、系统托盘、配置即时生效、应用排除和核心自动恢复。发布前必须验证普通窗口、全屏应用、快速切换、配置保存、原始按键恢复和打包后的独立运行。

配置导入导出、备份恢复和多套配置切换已加入 Win Mouse Fix。后续版本再加入高质量平滑滚动、连续手势动画、Pointer 和多设备独立配置；二维惯性滚动、多设备识别和系统指针设置应先制作小型样例，达到稳定结果后再列为正式功能。
