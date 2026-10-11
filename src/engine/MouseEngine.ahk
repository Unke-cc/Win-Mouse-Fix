#Requires AutoHotkey v2.0
#SingleInstance Force
#NoTrayIcon
#Warn All, StdOut
Persistent

A_MaxHotkeysPerInterval := 2000
A_HotkeyInterval := 2000

#Include lib\Json.ahk
#Include lib\ConfigManager.ahk
#Include lib\AppRuleManager.ahk
#Include lib\ActionManager.ahk
#Include lib\MouseManager.ahk

; 从这里启动。加载配置。接管鼠标。
class WinMouseFixEngine {
    ; GUI 发消息。0x8001 重载。0x8002 暂停。0x8003 恢复。
    static ReloadMessage := 0x8001
    static PauseMessage := 0x8002
    static ResumeMessage := 0x8003

    __New(validationOnly := false) {
        ; 目录含 config 则用之。否则读仓库。
        packagedDefaultPath := A_ScriptDir "\config\default.json"
        developmentDefaultPath := A_ScriptDir "\..\..\config\default.json"
        defaultPath := FileExist(packagedDefaultPath) ? packagedDefaultPath : developmentDefaultPath
        ; AppData 存放 config.json。
        userPath := A_AppData "\WinMouseFix\config.json"
        this.configManager := ConfigManager(defaultPath, userPath)
        this.config := this.configManager.Load()
        this.actionManager := ActionManager()
        this.appRuleManager := AppRuleManager(this.config)
        this.mouseManager := MouseManager(this.config, this.appRuleManager, this.actionManager)

        ; 参数要求自检。不注册热键。
        if validationOnly {
            this.ValidateConfiguration()
            return
        }

        this.mouseManager.Start()
        this.watchTimer := ObjBindMethod(this, "WatchConfiguration")
        ; 间隔 250 毫秒。检查文件是否变化。
        SetTimer(this.watchTimer, 250)
        OnMessage(WinMouseFixEngine.ReloadMessage, ObjBindMethod(this, "OnReloadMessage"))
        OnMessage(WinMouseFixEngine.PauseMessage, ObjBindMethod(this, "OnPauseMessage"))
        OnMessage(WinMouseFixEngine.ResumeMessage, ObjBindMethod(this, "OnResumeMessage"))
        OnExit(ObjBindMethod(this, "OnExit"))
    }

    ; 核对版本、映射、方向。非法则失败。
    ValidateConfiguration() {
        if this.config["configVersion"] != 2 {
            throw Error("Unsupported configVersion.")
        }
        if !(this.config["remaps"] is Array) {
            throw Error("remaps must be an array.")
        }
        if this.config["doubleClickSpeed"] != "fast"
            && this.config["doubleClickSpeed"] != "medium"
            && this.config["doubleClickSpeed"] != "slow" {
            throw Error("Unsupported doubleClickSpeed.")
        }
        if this.config["desktopSwipeDirection"] != "followMouse"
            && this.config["desktopSwipeDirection"] != "oppositeMouse" {
            throw Error("Unsupported desktopSwipeDirection.")
        }
        if this.config["scroll"]["takeOverWheel"] != true && this.config["scroll"]["takeOverWheel"] != false {
            throw Error("Invalid mouse wheel takeover setting.")
        }
        if this.config["scroll"]["inertia"] != true && this.config["scroll"]["inertia"] != false {
            throw Error("Invalid inertial scrolling setting.")
        }
        for remap in this.config["remaps"] {
            if remap["button"] = "" || !ConfigManager.TriggerNames.Has(remap["trigger"]) {
                throw Error("Invalid remap.")
            }
        }
        ; 拖向左且 followMouse，应发 Ctrl+Win+Right。
        if this.actionManager.DesktopNavigationShortcut("left", "followMouse") != "^#{Right}"
            || this.actionManager.DesktopNavigationShortcut("right", "followMouse") != "^#{Left}"
            || this.actionManager.DesktopNavigationShortcut("left", "oppositeMouse") != "^#{Left}"
            || this.actionManager.DesktopNavigationShortcut("right", "oppositeMouse") != "^#{Right}" {
            throw Error("Desktop navigation direction check failed.")
        }
        if this.actionManager.DesktopSwitchShortcut("left") != "^#{Left}"
            || this.actionManager.DesktopSwitchShortcut("right") != "^#{Right}" {
            throw Error("Desktop switch shortcut check failed.")
        }
        ; Progman、WorkerW 算桌面。Chrome_WidgetWin_1 不算。
        if !this.appRuleManager.IsDesktopSurfaceClass("Progman")
            || !this.appRuleManager.IsDesktopSurfaceClass("WorkerW")
            || this.appRuleManager.IsDesktopSurfaceClass("Chrome_WidgetWin_1") {
            throw Error("Desktop surface class check failed.")
        }
        ; holdMs、dragThresholdPx 非法则回到 500 与 12。
        fallbackConfig := this.configManager.Normalize(Map(
            "timing", Map("holdMs", "invalid", "dragThresholdPx", "invalid")
        ))
        if fallbackConfig["timing"]["holdMs"] != 500
            || fallbackConfig["timing"]["dragThresholdPx"] != 12 {
            throw Error("Timing fallback self-check failed.")
        }
        ; takeOverWheel 填 0 应变成 false。
        disabledWheelConfig := this.configManager.Normalize(Map("scroll", Map("takeOverWheel", 0)))
        if disabledWheelConfig["scroll"]["takeOverWheel"] != false {
            throw Error("Mouse wheel takeover fallback self-check failed.")
        }
        ; click 为 None 应改成 Original。
        if MouseManager.ResolveClickAction(Map("type", "None", "shortcut", ""))["type"] != "Original" {
            throw Error("Unmapped click fallback self-check failed.")
        }
        ; 逻辑按下且物理松开，才复位。
        if !MouseManager.ShouldResetStaleButton(Map("isDown", true), false)
            || MouseManager.ShouldResetStaleButton(Map("isDown", true), true)
            || MouseManager.ShouldResetStaleButton(Map("isDown", false), false) {
            throw Error("Mouse button state recovery check failed.")
        }
        return true
    }

    ; 指纹变了就重载。
    WatchConfiguration(*) {
        if this.configManager.HasChanged() {
            this.ReloadConfiguration()
        }
    }

    ReloadConfiguration(*) {
        newConfig := this.configManager.Load()
        this.config := newConfig
        this.appRuleManager.SetConfig(newConfig)
        this.mouseManager.SetConfig(newConfig)
        return true
    }

    ; 暂停后不再映射。调用 CancelAll。
    Pause(*) {
        this.appRuleManager.SetPaused(true)
        this.mouseManager.CancelAll(true)
    }

    Resume(*) {
        this.appRuleManager.SetPaused(false)
    }

    OnReloadMessage(*) {
        this.ReloadConfiguration()
        return 1
    }

    OnPauseMessage(*) {
        this.Pause()
        return 1
    }

    OnResumeMessage(*) {
        this.Resume()
        return 1
    }

    ; 停监视。停热键。
    OnExit(*) {
        try SetTimer(this.watchTimer, 0)
        try this.mouseManager.Stop()
        this.actionManager.ReleaseAll()
    }
}

; 命令行含 --validate 或 --self-test 则只自检。
validationOnly := false
for argument in A_Args {
    if argument = "--validate" || argument = "--self-test" {
        validationOnly := true
        break
    }
}

try {
    global engine := WinMouseFixEngine(validationOnly)
    if validationOnly {
        FileAppend("Win Mouse Fix engine validation passed.`n", "*")
        ExitApp(0)
    }
} catch Error as err {
    startupErrorText := err.Message "`nAt " err.File ":" err.Line "`n" err.Stack "`n"
    ; 自检失败退出码 1。运行失败弹窗后退出。
    FileAppend("Win Mouse Fix engine could not start: " startupErrorText, "**")
    if validationOnly {
        ExitApp(1)
    }
    MsgBox("Win Mouse Fix could not start.`n`n" err.Message, "Win Mouse Fix", "Iconx")
    ExitApp(1)
}


if !validationOnly {
    OnError(EngineRuntimeErrorHandler)
}

EngineRuntimeErrorHandler(err, mode) {
    try {
        loggedErrorText := "[" A_Now "] Runtime error: " err.Message " in " err.File " at line " err.Line "`n" err.Stack "`n"
        FileAppend(loggedErrorText, "**")
    }
    return 1 ; 拦截未捕获异常的致命弹窗，保持后台引擎稳定运行
}