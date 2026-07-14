# AutoHotkey v2 Mouse Engine

`MouseEngine.ahk` is the executable entry point for the Win Mouse Fix mouse engine.
It loads `%AppData%\WinMouseFix\config.json` when present and otherwise reads
the packaged `config/default.json`, then the repository `config/default.json`
during development. If none is available, safe built-in defaults are used.

Run a configuration and module check without registering mouse input:

```powershell
& "C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe" /ErrorStdOut `
  ".\src\engine\MouseEngine.ahk" --validate
```

While running, the engine checks the selected configuration file every 250 ms.
The WPF application owns the visible tray menu. It can request engine operations
by posting these messages to the engine window:

- `0x8001`: reload configuration
- `0x8002`: pause mappings
- `0x8003`: resume mappings

Physical inputs in `remaps` are limited to `MButton`, `XButton1`, and `XButton2`.
Mappings support click, double-click, hold, held-wheel, and held-drag.
The double-click choices are fast (150 ms), medium (250 ms), and slow (400 ms).
`desktopSwipeDirection` controls horizontal desktop navigation. `followMouse`
makes the desktop page animation follow the drag (`left` sends `Ctrl+Win+Right`),
while `oppositeMouse` sends the matching arrow direction.
Application exclusion and full-screen pause are checked
before every input sequence. When `scroll.smooth` is enabled, additional wheel
steps caused by a speed above `1.0` are spread over a short interval. All output
uses AutoHotkey's own send path so it cannot re-enter the wheel handler.

The GUI can suspend mappings while its input area is active by posting `0x8002`,
then post `0x8003` when capture ends. No separate capture protocol is required.
