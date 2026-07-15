#Requires AutoHotkey v2.0

class ActionManager {
    Execute(action, context := 0) {
        actionType := action["type"]
        switch actionType {
            case "None":
                return false
            case "Original":
                return this.SendOriginalFromContext(context)
            case "Back":
                SendEvent("{Browser_Back}")
            case "Forward":
                SendEvent("{Browser_Forward}")
            case "MiddleClick":
                SendEvent("{MButton}")
            case "PrimaryClick":
                SendEvent("{LButton}")
            case "SecondaryClick":
                SendEvent("{RButton}")
            case "TaskView":
                SendEvent("#{Tab}")
            case "ShowDesktop":
                SendEvent("#d")
            case "StartMenu":
                SendEvent("{LWin}")
            case "DesktopLeft":
                this.SendDesktopSwitch("left")
            case "DesktopRight":
                this.SendDesktopSwitch("right")
            case "AltTab":
                SendEvent("!{Tab}")
            case "CloseWindow":
                SendEvent("!{F4}")
            case "CloseTab":
                SendEvent("^w")
            case "NextTab":
                SendEvent("^{Tab}")
            case "PreviousTab":
                SendEvent("^+{Tab}")
            case "VolumeUp":
                SendEvent("{Volume_Up}")
            case "VolumeDown":
                SendEvent("{Volume_Down}")
            case "VolumeMute":
                SendEvent("{Volume_Mute}")
            case "MediaPlayPause":
                SendEvent("{Media_Play_Pause}")
            case "FastScroll":
                if !(context is Map) || !context.Has("wheelDirection") {
                    return false
                }
                this.SendWheel(context["wheelDirection"], 4)
            case "Zoom":
                if !(context is Map) || !context.Has("wheelDirection") {
                    return false
                }
                SendEvent(context["wheelDirection"] = "down" ? "^{WheelDown}" : "^{WheelUp}")
            case "VolumeControl":
                return this.ExecuteWheelPair("VolumeUp", "VolumeDown", context)
            case "TabNavigation":
                return this.ExecuteWheelPair("PreviousTab", "NextTab", context)
            case "BrowserNavigation":
                return this.ExecuteWheelPair("Back", "Forward", context)
            case "DesktopSwitch":
                return this.ExecuteWheelPair("DesktopLeft", "DesktopRight", context)
            case "DesktopStartMenu":
                return this.ExecuteWheelPair("ShowDesktop", "StartMenu", context)
            case "ScrollMove":
                return this.ScrollFromDrag(context)
            case "DesktopNavigation":
                return this.NavigateFromDrag(context)
            case "BrowserTabNavigation":
                return this.NavigateBrowserFromDrag(context)
            case "CustomShortcut":
                shortcut := this.ToAutoHotkeyShortcut(action["shortcut"])
                if shortcut = "" {
                    return false
                }
                SendEvent(shortcut)
            default:
                return false
        }
        return true
    }

    ExecuteWheelPair(upActionType, downActionType, context) {
        if !(context is Map) || !context.Has("wheelDirection") {
            return false
        }
        actionType := context["wheelDirection"] = "down" ? downActionType : upActionType
        return this.Execute(Map("type", actionType, "shortcut", ""), context)
    }

    ScrollFromDrag(context) {
        if !(context is Map) || !context.Has("deltaX") || !context.Has("deltaY") {
            return false
        }
        deltaX := context["deltaX"]
        deltaY := context["deltaY"]
        horizontalSteps := Floor(Abs(deltaX) / 8)
        verticalSteps := Floor(Abs(deltaY) / 8)
        if horizontalSteps > 0 {
            this.SendHorizontalWheel(deltaX > 0 ? "left" : "right", horizontalSteps)
        }
        if verticalSteps > 0 {
            this.SendWheel(deltaY > 0 ? "up" : "down", verticalSteps)
        }
        return horizontalSteps > 0 || verticalSteps > 0
    }

    NavigateFromDrag(context) {
        if !(context is Map) || !context.Has("dragDirection") {
            return false
        }
        dragDirection := context["dragDirection"]
        if dragDirection = "left" || dragDirection = "right" {
            this.SendDesktopSwitch(dragDirection = "left" ? "right" : "left")
            return true
        }
        shortcut := this.DesktopNavigationShortcut(dragDirection)
        if shortcut = "" {
            return false
        }
        SendEvent(shortcut)
        return true
    }

    NavigateBrowserFromDrag(context) {
        if !(context is Map) || !context.Has("dragDirection") {
            return false
        }
        switch context["dragDirection"] {
            case "up":
                actionType := "PreviousTab"
            case "down":
                actionType := "NextTab"
            case "left":
                actionType := "Back"
            case "right":
                actionType := "Forward"
            default:
                return false
        }
        return this.Execute(Map("type", actionType, "shortcut", ""), context)
    }

    DesktopNavigationShortcut(dragDirection, swipeDirection := "followMouse") {
        followMouse := swipeDirection != "oppositeMouse"
        switch dragDirection {
            case "left":
                return followMouse ? "^#{Right}" : "^#{Left}"
            case "right":
                return followMouse ? "^#{Left}" : "^#{Right}"
            case "up":
                return "#{Tab}"
            case "down":
                return "#d"
            default:
                return ""
        }
    }

    SendOriginalFromContext(context) {
        if !(context is Map) {
            return false
        }
        if context.Has("wheelDirection") {
            this.SendWheel(context["wheelDirection"])
            return true
        }
        if !context.Has("button") {
            return false
        }
        clickCount := context.Has("clickCount") ? context["clickCount"] : 1
        loop Max(1, clickCount) {
            SendEvent("{Blind}{" context["button"] "}")
        }
        return true
    }

    SendButtonDown(button) {
        SendEvent("{Blind}{" button " down}")
    }

    SendButtonUp(button) {
        SendEvent("{Blind}{" button " up}")
    }

    SendWheel(direction, count := 1) {
        wheelKey := direction = "down" ? "WheelDown" : "WheelUp"
        SendEvent("{Blind}{" wheelKey " " Max(1, count) "}")
    }

    SendHorizontalWheel(direction, count := 1) {
        wheelKey := direction = "right" ? "WheelRight" : "WheelLeft"
        SendEvent("{Blind}{" wheelKey " " Max(1, count) "}")
    }

    SendDesktopSwitch(direction) {
        SendInput(this.DesktopSwitchShortcut(direction))
    }

    DesktopSwitchShortcut(direction) => direction = "right" ? "^#{Right}" : "^#{Left}"

    ReleaseAll() {
        ; MVP actions use complete key presses and therefore keep no synthetic key held.
    }

    ToAutoHotkeyShortcut(shortcut) {
        shortcut := Trim(shortcut)
        if shortcut = "" {
            return ""
        }
        if RegExMatch(shortcut, "[\^!#{}]") {
            return shortcut
        }

        parts := StrSplit(shortcut, "+")
        modifiers := ""
        key := ""
        for part in parts {
            token := StrLower(Trim(part))
            switch token {
                case "ctrl", "control":
                    modifiers .= "^"
                case "alt":
                    modifiers .= "!"
                case "shift":
                    modifiers .= "+"
                case "win", "windows":
                    modifiers .= "#"
                default:
                    key := Trim(part)
            }
        }
        if key = "" {
            return ""
        }
        return modifiers (StrLen(key) = 1 ? key : "{" key "}")
    }
}
