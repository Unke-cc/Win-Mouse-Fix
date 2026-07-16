# Win Mouse Fix 鼠标功能核心

`MouseEngine.ahk` 是 Win Mouse Fix 鼠标功能核心的入口文件，当前配置格式为 `configVersion: 2`。
它优先读取 `%AppData%\WinMouseFix\config.json`；没有用户配置时读取安装包中的 `config/default.json`，开发环境则读取仓库中的默认配置。

Run a configuration and module check without registering mouse input:

```powershell
& "C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe" /ErrorStdOut `
  ".\src\engine\MouseEngine.ahk" --validate
```

运行时核心每 250 毫秒检查一次当前配置文件。
The WPF application owns the visible tray menu. It can request engine operations
by posting these messages to the engine window:

- `0x8001`: reload configuration
- `0x8002`: pause mappings
- `0x8003`: resume mappings

`remaps` 当前支持 `MButton`、`XButton1` 和 `XButton2`。
映射支持点击、双击、长按、按住并滚动和按住并拖动。
按住并滚动使用一个融合动作处理上下方向；按住并拖动使用一个融合动作处理上下左右。
The double-click choices are fast (150 ms), medium (250 ms), and slow (400 ms).
应用排除和全屏暂停会在每次输入前检查。
滚轮修饰键支持由 Ctrl、Alt、Shift、Win 组成的组合；快速滚动和精确滚动发送不带对应修饰键的普通滚轮，缩放保留 Ctrl 组合。
触摸板连续高频滚轮输入使用保护策略，达到阈值后暂时停止软件滚轮接管，让系统恢复原生滚动。
`scroll.smooth` 开启后，额外滚轮步进会以短间隔发送。

GUI 可通过 `0x8002` 暂停映射，在录入区域结束后通过 `0x8003` 恢复映射；当前不使用独立的状态确认协议。
