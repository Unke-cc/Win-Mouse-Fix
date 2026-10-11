#Requires AutoHotkey v2.0

; 执行映射。输出按键、滚轮、快捷键。
class ActionManager {
    Execute(action, context := 0) {
        actionType := action["type"]
        switch actionType {
            case "None":
                return false
            case "Original":
                return this.SendOriginalFromContext(context)
            case "Back":
                SendEvent("{Browser_Back}") ; 后退。
            case "Forward":
                SendEvent("{Browser_Forward}") ; 前进。
            case "MiddleClick":
                SendEvent("{MButton}") ; 中键。
            case "PrimaryClick":
                SendEvent("{LButton}") ; 左键。
            case "SecondaryClick":
                SendEvent("{RButton}") ; 右键。
            case "TaskView":
                SendEvent("#{Tab}") ; Win+Tab。开任务视图。
            case "ShowDesktop":
                SendEvent("#d") ; Win+D。显示桌面。
            case "StartMenu":
                SendEvent("{LWin}") ; 左 Win。开开始菜单。
            case "DesktopLeft":
                this.SendDesktopSwitch("left") ; Ctrl+Win+Left。
            case "DesktopRight":
                this.SendDesktopSwitch("right") ; Ctrl+Win+Right。
            case "AltTab":
                SendEvent("!{Tab}") ; Alt+Tab。切换窗口。
            case "CloseWindow":
                SendEvent("!{F4}") ; Alt+F4。关闭窗口。
            case "CloseTab":
                SendEvent("^w") ; Ctrl+W。关闭标签。
            case "NextTab":
                SendEvent("^{Tab}") ; Ctrl+Tab。往后切标签。
            case "PreviousTab":
                SendEvent("^+{Tab}") ; Ctrl+Shift+Tab。往前切标签。
            case "VolumeUp":
                SendEvent("{Volume_Up}") ; 音量加。
            case "VolumeDown":
                SendEvent("{Volume_Down}") ; 音量减。
            case "VolumeMute":
                SendEvent("{Volume_Mute}") ; 静音。
            case "MediaPlayPause":
                SendEvent("{Media_Play_Pause}") ; 播放或暂停。
            case "FastScroll":
                if !(context is Map) || !context.Has("wheelDirection") {
                    return false
                }
                ; 一次滚 4 格。
                this.SendWheel(context["wheelDirection"], 4)
            case "Zoom":
                if !(context is Map) || !context.Has("wheelDirection") {
                    return false
                }
                ; 下：Ctrl+WheelDown。上：Ctrl+WheelUp。
                SendEvent(context["wheelDirection"] = "down" ? "^{WheelDown}" : "^{WheelUp}")
            case "VolumeControl":
                ; 上：音量加。下：音量减。
                return this.ExecuteWheelPair("VolumeUp", "VolumeDown", context)
            case "TabNavigation":
                ; 上：Ctrl+Shift+Tab。下：Ctrl+Tab。
                return this.ExecuteWheelPair("PreviousTab", "NextTab", context)
            case "BrowserNavigation":
                ; 上：后退。下：前进。
                return this.ExecuteWheelPair("Back", "Forward", context)
            case "DesktopSwitch":
                ; 上：Ctrl+Win+Left。下：Ctrl+Win+Right。
                return this.ExecuteWheelPair("DesktopLeft", "DesktopRight", context)
            case "DesktopStartMenu":
                ; 上：Win+D。下：左 Win。
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

    ; 上滚用前者。下滚用后者。
    ExecuteWheelPair(upActionType, downActionType, context) {
        if !(context is Map) || !context.Has("wheelDirection") {
            return false
        }
        actionType := context["wheelDirection"] = "down" ? downActionType : upActionType
        return this.Execute(Map("type", actionType, "shortcut", ""), context)
    }

    ; 位移满 8 像素，滚一格。
    ; 鼠标右移发 WheelLeft。下移发 WheelDown。
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

    ; 左右拖会反向切桌面。上拖开任务视图。下拖显桌面。
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
                actionType := "PreviousTab" ; Ctrl+Shift+Tab。
            case "down":
                actionType := "NextTab" ; Ctrl+Tab。
            case "left":
                actionType := "Back" ; 后退。
            case "right":
                actionType := "Forward" ; 前进。
            default:
                return false
        }
        return this.Execute(Map("type", actionType, "shortcut", ""), context)
    }

    ; followMouse 时左右对调。上为 Win+Tab。下为 Win+D。
    DesktopNavigationShortcut(dragDirection, swipeDirection := "followMouse") {
        followMouse := swipeDirection != "oppositeMouse"
        switch dragDirection {
            case "left":
                ; followMouse：Ctrl+Win+Right。oppositeMouse：Ctrl+Win+Left。
                return followMouse ? "^#{Right}" : "^#{Left}"
            case "right":
                ; followMouse：Ctrl+Win+Left。oppositeMouse：Ctrl+Win+Right。
                return followMouse ? "^#{Left}" : "^#{Right}"
            case "up":
                return "#{Tab}" ; Win+Tab。
            case "down":
                return "#d" ; Win+D。
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
        ; Blind 保留修饰键。
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

    ; preserveModifiers 为假时去掉 Blind，避免 Alt/Win 改写滚轮。
    SendWheel(direction, count := 1, preserveModifiers := true) {
        wheelKey := direction = "down" ? "WheelDown" : "WheelUp"
        prefix := preserveModifiers ? "{Blind}" : ""
        SendEvent(prefix "{" wheelKey " " Max(1, count) "}")
    }

    SendHorizontalWheel(direction, count := 1) {
        wheelKey := direction = "right" ? "WheelRight" : "WheelLeft"
        SendEvent("{Blind}{" wheelKey " " Max(1, count) "}")
    }

    SendDesktopSwitch(direction) {
        SendInput(this.DesktopSwitchShortcut(direction))
    }

    ; 向右：Ctrl+Win+Right。向左：Ctrl+Win+Left。
    DesktopSwitchShortcut(direction) => direction = "right" ? "^#{Right}" : "^#{Left}"

    ReleaseAll() {
        ; 按键一次发完。不保持按下状态。
    }

    ; 已含 ^ ! # { } 则视为 AHK 语法。
    ; 否则 Ctrl Alt Shift Win 转成 ^ ! + #。
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
