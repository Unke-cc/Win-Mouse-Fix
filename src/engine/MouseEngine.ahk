#Requires AutoHotkey v2.0
#SingleInstance Force
#NoTrayIcon
#Warn All, StdOut
Persistent

#Include lib\Json.ahk
#Include lib\ConfigManager.ahk
#Include lib\AppRuleManager.ahk
#Include lib\ActionManager.ahk
#Include lib\MouseManager.ahk

class WinMouseFixEngine {
    static ReloadMessage := 0x8001
    static PauseMessage := 0x8002
    static ResumeMessage := 0x8003

    __New(validationOnly := false) {
        packagedDefaultPath := A_ScriptDir "\config\default.json"
        developmentDefaultPath := A_ScriptDir "\..\..\config\default.json"
        defaultPath := FileExist(packagedDefaultPath) ? packagedDefaultPath : developmentDefaultPath
        userPath := A_AppData "\WinMouseFix\config.json"
        this.configManager := ConfigManager(defaultPath, userPath)
        this.config := this.configManager.Load()
        this.actionManager := ActionManager()
        this.appRuleManager := AppRuleManager(this.config)
        this.mouseManager := MouseManager(this.config, this.appRuleManager, this.actionManager)

        if validationOnly {
            this.ValidateConfiguration()
            return
        }

        this.mouseManager.Start()
        this.watchTimer := ObjBindMethod(this, "WatchConfiguration")
        SetTimer(this.watchTimer, 250)
        OnMessage(WinMouseFixEngine.ReloadMessage, ObjBindMethod(this, "OnReloadMessage"))
        OnMessage(WinMouseFixEngine.PauseMessage, ObjBindMethod(this, "OnPauseMessage"))
        OnMessage(WinMouseFixEngine.ResumeMessage, ObjBindMethod(this, "OnResumeMessage"))
        OnExit(ObjBindMethod(this, "OnExit"))
    }

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
        for remap in this.config["remaps"] {
            if remap["button"] = "" || !ConfigManager.TriggerNames.Has(remap["trigger"]) {
                throw Error("Invalid remap.")
            }
        }
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
        if !this.appRuleManager.IsDesktopSurfaceClass("Progman")
            || !this.appRuleManager.IsDesktopSurfaceClass("WorkerW")
            || this.appRuleManager.IsDesktopSurfaceClass("Chrome_WidgetWin_1") {
            throw Error("Desktop surface class check failed.")
        }
        fallbackConfig := this.configManager.Normalize(Map(
            "timing", Map("holdMs", "invalid", "dragThresholdPx", "invalid")
        ))
        if fallbackConfig["timing"]["holdMs"] != 500
            || fallbackConfig["timing"]["dragThresholdPx"] != 12 {
            throw Error("Timing fallback self-check failed.")
        }
        if MouseManager.ResolveClickAction(Map("type", "None", "shortcut", ""))["type"] != "Original" {
            throw Error("Unmapped click fallback self-check failed.")
        }
        if !MouseManager.ShouldResetStaleButton(Map("isDown", true), false)
            || MouseManager.ShouldResetStaleButton(Map("isDown", true), true)
            || MouseManager.ShouldResetStaleButton(Map("isDown", false), false) {
            throw Error("Mouse button state recovery check failed.")
        }
        return true
    }

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

    OnExit(*) {
        try SetTimer(this.watchTimer, 0)
        try this.mouseManager.Stop()
        this.actionManager.ReleaseAll()
    }
}

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
    errorDetails := err.Message "`nAt " err.File ":" err.Line "`n" err.Stack "`n"
    FileAppend("Win Mouse Fix engine could not start: " errorDetails, "**")
    if validationOnly {
        ExitApp(1)
    }
    MsgBox("Win Mouse Fix could not start.`n`n" err.Message, "Win Mouse Fix", "Iconx")
    ExitApp(1)
}
