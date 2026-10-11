# Win Mouse Fix 鼠标功能核心

`MouseEngine.ahk` 是核心入口，配置格式 `configVersion: 2`。
优先读 `%AppData%\WinMouseFix\config.json`；没有用户配置时读安装目录 `config/default.json`，开发时读仓库默认配置。

不注册鼠标输入的自检：

```powershell
& "C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe" /ErrorStdOut `
  ".\src\engine\MouseEngine.ahk" --validate
```

运行时每 250 ms 检查配置文件。设置窗口可通过引擎窗口消息请求：

- `0x8001`：重新载入配置
- `0x8002`：暂停映射
- `0x8003`：恢复映射

`remaps` 只支持 `MButton`、`XButton1`、`XButton2`。左键、右键不得注册手势热键。
映射：点击、双击、长按、按住滚动、按住拖动。
双击间隔：快 150 ms、中 250 ms、慢 400 ms。
应用排除和全屏暂停在每次输入前检查。全屏暂停与登录运行由配置强制为开。

滚轮热键：`takeOverWheel` 为真时常驻；为假时仅在已映射按键按下时挂载，平时放行系统滚轮和触控板。
普通滚动不再按键盘修饰键改写。按住中键/侧键时走按钮手势，不走平滑/惯性。
`scroll.smooth`：额外滚轮步进短间隔发送。
`scroll.inertia`：同向连续 ≥4 格且平均间隔 < 60 ms 才注入动量，阻尼 0.72，低于 0.25 停止；优先于 `smooth`。
热键频率上限 2000 次 / 2 秒，并挂 `OnError`。

位移检测定时器仅在已映射按键按下时以 15 ms 运行。当前没有独立的状态确认协议。
