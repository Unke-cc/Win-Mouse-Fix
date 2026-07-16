#Requires AutoHotkey v2.0

class MouseManager {
    __New(config, appRuleManager, actionManager) {
        this.appRuleManager := appRuleManager
        this.actionManager := actionManager
        this.states := Map()
        this.registeredButtons := []
        this.started := false
        this.sequence := 0
        this.scrollCarry := 0.0
        this.scrollMode := "regular"
        this.smoothQueue := []
        this.smoothTimer := ObjBindMethod(this, "ProcessSmoothWheel")
        this.motionTimer := ObjBindMethod(this, "CheckMotionAndContext")
        this.wheelRestoreTimer := ObjBindMethod(this, "RestoreWheelHotkeys")
        this.wheelHotkeysSuspended := false
        this.wheelBurstStart := 0
        this.wheelBurstCount := 0
        this.wheelBurstWindowMs := 300
        this.wheelBurstThreshold := 20
        this.wheelSuspendMs := 750
        this.lastCanHandle := appRuleManager.ShouldHandle()
        this.SetConfig(config)
    }

    SetConfig(config) {
        wasStarted := this.started
        if wasStarted {
            this.UnregisterHotkeys()
        }
        this.CancelAll(true)
        this.config := config
        this.scrollConfig := config["scroll"]
        this.multiClickMs := config["timing"]["multiClickMs"]
        this.holdMs := config["timing"]["holdMs"]
        this.dragThresholdPx := config["timing"]["dragThresholdPx"]
        this.remaps := this.BuildRemapIndex(config["remaps"])
        this.RebuildStates()
        this.scrollCarry := 0.0
        this.scrollMode := "regular"
        this.smoothQueue := []
        SetTimer(this.smoothTimer, 0)
        this.ResetWheelFilter()
        if wasStarted {
            this.RegisterHotkeys()
        }
    }

    BuildRemapIndex(remaps) {
        result := Map()
        for remap in remaps {
            button := remap["button"]
            if !result.Has(button) {
                result[button] := Map()
            }
            result[button][remap["trigger"]] := remap["action"]
        }
        return result
    }

    RebuildStates() {
        this.states := Map()
        for buttonName, triggers in this.remaps {
            hasAction := false
            for triggerName, action in triggers {
                if action["type"] != "None" {
                    hasAction := true
                    break
                }
            }
            if !hasAction {
                continue
            }
            state := Map(
                "isDown", false,
                "mode", "idle",
                "consumed", false,
                "gestureUsed", false,
                "interaction", "none",
                "wheelDirections", Map(),
                "pendingClicks", 0,
                "startX", 0,
                "startY", 0,
                "lastX", 0,
                "lastY", 0,
                "sequence", 0
            )
            state["clickTimer"] := ObjBindMethod(this, "FinalizeClicks", buttonName)
            state["holdTimer"] := ObjBindMethod(this, "OnHoldTimer", buttonName)
            this.states[buttonName] := state
        }
    }

    Start() {
        if this.started {
            return
        }
        this.started := true
        this.RegisterHotkeys()
    }

    RegisterHotkeys() {
        this.registeredButtons := []
        for buttonName, state in this.states {
            try {
                Hotkey("$*" buttonName, ObjBindMethod(this, "OnButtonDown", buttonName), "On")
                Hotkey("$*" buttonName " Up", ObjBindMethod(this, "OnButtonUp", buttonName), "On")
                this.registeredButtons.Push(buttonName)
            } catch Error as registrationError {
                try Hotkey("$*" buttonName, "Off")
                FileAppend("Ignoring unsupported input " buttonName ": " registrationError.Message "`n", "**")
            }
        }
        Hotkey("$*WheelUp", ObjBindMethod(this, "OnWheel", "up"), "On")
        Hotkey("$*WheelDown", ObjBindMethod(this, "OnWheel", "down"), "On")
        this.wheelHotkeysSuspended := false
        SetTimer(this.motionTimer, 15)
    }

    Stop() {
        if !this.started {
            return
        }
        this.UnregisterHotkeys()
        this.started := false
        this.CancelAll()
    }

    UnregisterHotkeys() {
        SetTimer(this.motionTimer, 0)
        SetTimer(this.smoothTimer, 0)
        SetTimer(this.wheelRestoreTimer, 0)
        this.smoothQueue := []
        this.wheelHotkeysSuspended := false
        for buttonName in this.registeredButtons {
            try Hotkey("$*" buttonName, "Off")
            try Hotkey("$*" buttonName " Up", "Off")
        }
        this.registeredButtons := []
        try Hotkey("$*WheelUp", "Off")
        try Hotkey("$*WheelDown", "Off")
    }

    OnButtonDown(buttonName, *) {
        state := this.states[buttonName]
        if state["isDown"] {
            return
        }

        if !this.appRuleManager.ShouldHandle() {
            this.CancelPending(state)
            state["isDown"] := true
            state["mode"] := "native"
            this.actionManager.SendButtonDown(buttonName)
            return
        }

        if state["pendingClicks"] > 0 {
            SetTimer(state["clickTimer"], 0)
        }
        state["isDown"] := true
        state["mode"] := "mapped"
        state["consumed"] := false
        state["gestureUsed"] := false
        state["interaction"] := "none"
        state["wheelDirections"] := Map()
        MouseGetPos(&mouseX, &mouseY)
        state["startX"] := mouseX
        state["startY"] := mouseY
        state["lastX"] := mouseX
        state["lastY"] := mouseY
        this.sequence += 1
        state["sequence"] := this.sequence
        if this.HasAction(buttonName, "hold") {
            SetTimer(state["holdTimer"], -this.holdMs)
        }
    }

    OnButtonUp(buttonName, *) {
        state := this.states[buttonName]
        if !state["isDown"] {
            return
        }

        SetTimer(state["holdTimer"], 0)
        if state["mode"] = "native" {
            this.actionManager.SendButtonUp(buttonName)
            this.ResetPhysicalState(state)
            return
        }
        if state["mode"] = "suppressed" {
            this.ResetPhysicalState(state)
            return
        }

        state["isDown"] := false
        state["mode"] := "idle"
        if state["consumed"] || state["gestureUsed"] {
            state["consumed"] := false
            state["gestureUsed"] := false
            this.CancelPending(state)
            return
        }

        state["pendingClicks"] += 1
        if state["pendingClicks"] >= 2 || !this.ShouldWaitForMoreClicks(buttonName) {
            this.FinalizeClicks(buttonName)
        } else {
            SetTimer(state["clickTimer"], -this.multiClickMs)
        }
    }

    OnHoldTimer(buttonName, *) {
        state := this.states[buttonName]
        if !state["isDown"] || state["mode"] != "mapped" || state["consumed"] || state["interaction"] != "none" {
            return
        }
        if !this.appRuleManager.ShouldHandle() {
            this.SuppressUntilRelease(state)
            return
        }

        if this.actionManager.Execute(this.GetAction(buttonName, "hold"), Map(
            "button", buttonName,
            "trigger", "hold"
        )) {
            state["consumed"] := true
            state["interaction"] := "hold"
            state["gestureUsed"] := true
            this.CancelPending(state)
        }
    }

    FinalizeClicks(buttonName, *) {
        state := this.states[buttonName]
        clickCount := state["pendingClicks"]
        state["pendingClicks"] := 0
        SetTimer(state["clickTimer"], 0)
        if clickCount <= 0 || !this.appRuleManager.ShouldHandle() {
            return
        }

        triggerName := clickCount = 1 ? "click" : "doubleClick"
        action := MouseManager.ResolveClickAction(this.GetAction(buttonName, triggerName))
        this.actionManager.Execute(action, Map(
            "button", buttonName,
            "clickCount", clickCount,
            "trigger", triggerName
        ))
    }

    static ResolveClickAction(action) {
        return action["type"] = "None"
            ? Map("type", "Original", "shortcut", "")
            : action
    }

    static ShouldResetStaleButton(state, physicalDown) {
        return state["isDown"] && !physicalDown
    }

    CheckMotionAndContext(*) {
        for buttonName, state in this.states {
            if !MouseManager.ShouldResetStaleButton(state, GetKeyState(buttonName, "P")) {
                continue
            }
            SetTimer(state["clickTimer"], 0)
            SetTimer(state["holdTimer"], 0)
            if state["mode"] = "native" {
                this.actionManager.SendButtonUp(buttonName)
            }
            this.ResetPhysicalState(state)
        }

        canHandle := this.appRuleManager.ShouldHandle()
        if !canHandle && this.lastCanHandle {
            this.CancelAll(true)
        }
        this.lastCanHandle := canHandle
        if !canHandle {
            return
        }

        MouseGetPos(&mouseX, &mouseY)
        for buttonName, state in this.states {
            if !state["isDown"] || state["mode"] != "mapped" || state["consumed"]
                || state["interaction"] = "hold" || state["interaction"] = "wheel" {
                continue
            }
            deltaX := mouseX - state["lastX"]
            deltaY := mouseY - state["lastY"]
            totalX := mouseX - state["startX"]
            totalY := mouseY - state["startY"]
            threshold := state["gestureUsed"] ? 8 : this.dragThresholdPx
            if Abs(totalX) < threshold && Abs(totalY) < threshold
                && Abs(deltaX) < threshold && Abs(deltaY) < threshold {
                continue
            }

            if Abs(totalX) >= Abs(totalY) {
                direction := totalX < 0 ? "left" : "right"
            } else {
                direction := totalY < 0 ? "up" : "down"
            }
            action := this.GetDragAction(buttonName, direction)
            if action["type"] = "None" {
                continue
            }
            if this.actionManager.Execute(action, Map(
                "button", buttonName,
                "trigger", "holdDrag",
                "dragDirection", direction,
                "deltaX", deltaX,
                "deltaY", deltaY
            )) {
                state["gestureUsed"] := true
                state["interaction"] := "drag"
                SetTimer(state["holdTimer"], 0)
                this.CancelPending(state)
                state["lastX"] := mouseX
                state["lastY"] := mouseY
                if action["type"] != "ScrollMove" {
                    state["consumed"] := true
                }
            }
        }
    }

    OnWheel(direction, *) {
        if this.ShouldFilterTouchpadBurst() {
            this.SuspendWheelHotkeys()
            return
        }
        if this.appRuleManager.ShouldHandle() && this.TryModifierWheel(direction, true) {
            return
        }
        activeButton := this.FindActiveHeldButton()
        if activeButton != "" {
            state := this.states[activeButton]
            if !this.appRuleManager.ShouldHandle() {
                this.SuppressUntilRelease(state)
                this.actionManager.SendWheel(direction)
                return
            }
            if state["interaction"] = "hold" || state["interaction"] = "drag" {
                this.SendRegularWheel(direction)
                return
            }
            action := this.GetWheelAction(activeButton, direction)
            if action["type"] = "None" {
                if state["interaction"] = "wheel" {
                    return
                }
            } else {
                repeatMode := this.GetWheelRepeatMode(action)
                if repeatMode = "once" && state["wheelDirections"].Has("once") {
                    return
                }
                if repeatMode = "perDirection" && state["wheelDirections"].Has(direction) {
                    return
                }
                if this.ExecuteWheelAction(action, direction, Map(
                    "button", activeButton,
                    "wheelDirection", direction,
                    "trigger", direction = "up" ? "wheelUp" : "wheelDown"
                )) {
                    state["interaction"] := "wheel"
                    state["consumed"] := true
                    state["gestureUsed"] := true
                    state["wheelDirections"][repeatMode = "once" ? "once" : direction] := true
                    SetTimer(state["holdTimer"], 0)
                    this.CancelPending(state)
                    return
                }
            }
        }
        if this.appRuleManager.ShouldHandle() && this.TryModifierWheel(direction) {
            return
        }
        this.SendRegularWheel(direction)
    }

    ShouldFilterTouchpadBurst() {
        ; Windows exposes many touchpad gestures as a high-frequency wheel stream.
        ; AHK's wheel hotkey does not include the originating device, so only
        ; neutral, unmodified bursts are treated as touchpad input here.
        if !this.appRuleManager.ShouldHandle() {
            this.wheelBurstStart := 0
            this.wheelBurstCount := 0
            return false
        }
        if this.FindActiveHeldButton() != "" || this.HasPressedScrollModifier() {
            this.wheelBurstStart := 0
            this.wheelBurstCount := 0
            return false
        }

        now := A_TickCount
        if this.wheelBurstStart = 0 || now - this.wheelBurstStart > this.wheelBurstWindowMs {
            this.wheelBurstStart := now
            this.wheelBurstCount := 0
        }
        this.wheelBurstCount += 1
        return this.wheelBurstCount >= this.wheelBurstThreshold
    }

    HasPressedScrollModifier() {
        for _, modifier in [
            this.scrollConfig["horizontalModifier"],
            this.scrollConfig["fastModifier"],
            this.scrollConfig["precisionModifier"],
            this.scrollConfig["zoomModifier"]
        ] {
            if modifier != "none" && this.IsModifierPressed(modifier) {
                return true
            }
        }
        return false
    }

    SuspendWheelHotkeys() {
        if this.wheelHotkeysSuspended {
            return
        }
        try Hotkey("$*WheelUp", "Off")
        try Hotkey("$*WheelDown", "Off")
        this.wheelHotkeysSuspended := true
        SetTimer(this.wheelRestoreTimer, -this.wheelSuspendMs)
    }

    RestoreWheelHotkeys(*) {
        if !this.started {
            return
        }
        Hotkey("$*WheelUp", ObjBindMethod(this, "OnWheel", "up"), "On")
        Hotkey("$*WheelDown", ObjBindMethod(this, "OnWheel", "down"), "On")
        this.wheelHotkeysSuspended := false
        this.wheelBurstStart := 0
        this.wheelBurstCount := 0
    }

    ResetWheelFilter() {
        SetTimer(this.wheelRestoreTimer, 0)
        this.wheelHotkeysSuspended := false
        this.wheelBurstStart := 0
        this.wheelBurstCount := 0
    }

    TryModifierWheel(direction, mouseOnly := false) {
        matches := [
            Map("modifier", this.scrollConfig["horizontalModifier"], "action", "horizontal"),
            Map("modifier", this.scrollConfig["fastModifier"], "action", "fast"),
            Map("modifier", this.scrollConfig["precisionModifier"], "action", "precision"),
            Map("modifier", this.scrollConfig["zoomModifier"], "action", "zoom")
        ]
        selected := ""
        selectedLength := 0
        for match in matches {
            modifier := match["modifier"]
            if mouseOnly && !this.ModifierContainsMouseButton(modifier) {
                continue
            }
            if this.IsModifierPressed(modifier) {
                length := this.ModifierTokenCount(modifier)
                if length > selectedLength {
                    selected := match["action"]
                    selectedLength := length
                }
            }
        }

        if selected = "horizontal" {
            this.BeginScrollMode("horizontal")
            direction := this.ApplyScrollDirection(direction)
            this.actionManager.SendHorizontalWheel(direction = "down" ? "right" : "left")
            return true
        }
        if selected = "fast" {
            this.SendRegularWheel(direction, 4.0, "fast")
            return true
        }
        if selected = "precision" {
            this.SendRegularWheel(direction, 0.25, "precision")
            return true
        }
        if selected = "zoom" {
            this.BeginScrollMode("zoom")
            direction := this.ApplyScrollDirection(direction)
            return this.actionManager.Execute(Map("type", "Zoom", "shortcut", ""), Map(
                "wheelDirection", direction,
                "trigger", "modifierWheel"
            ))
        }
        return false
    }

    IsModifierPressed(modifier) {
        if modifier = "" || modifier = "none" {
            return false
        }
        for token in StrSplit(modifier, "+") {
            switch Trim(token) {
                case "ctrl":
                    if !(GetKeyState("LControl", "P") || GetKeyState("RControl", "P")) {
                        return false
                    }
                case "alt":
                    if !(GetKeyState("LAlt", "P") || GetKeyState("RAlt", "P")) {
                        return false
                    }
                case "shift":
                    if !(GetKeyState("LShift", "P") || GetKeyState("RShift", "P")) {
                        return false
                    }
                case "win":
                    if !(GetKeyState("LWin", "P") || GetKeyState("RWin", "P")) {
                        return false
                    }
                case "mbutton":
                    if !GetKeyState("MButton", "P") {
                        return false
                    }
                case "xbutton1":
                    if !GetKeyState("XButton1", "P") {
                        return false
                    }
                case "xbutton2":
                    if !GetKeyState("XButton2", "P") {
                        return false
                    }
                default:
                    return false
            }
        }
        return true
    }

    ModifierTokenCount(modifier) {
        return modifier = "" || modifier = "none" ? 0 : StrSplit(modifier, "+").Length
    }

    ModifierContainsMouseButton(modifier) {
        for token in StrSplit(modifier, "+") {
            if Trim(token) = "mbutton" || Trim(token) = "xbutton1" || Trim(token) = "xbutton2" {
                return true
            }
        }
        return false
    }

    ExecuteWheelAction(action, direction, context) {
        switch action["type"] {
            case "FastScroll":
                this.SendRegularWheel(direction, 4.0, "fast")
                return true
            case "PrecisionScroll":
                this.SendRegularWheel(direction, 0.25, "precision")
                return true
            default:
                return this.actionManager.Execute(action, context)
        }
    }

    ApplyScrollDirection(direction) {
        return this.scrollConfig["reverse"] ? (direction = "up" ? "down" : "up") : direction
    }

    SendRegularWheel(direction, speedScale := 1.0, scrollMode := "regular") {
        if !this.appRuleManager.ShouldHandle() {
            this.actionManager.SendWheel(direction)
            return
        }
        this.BeginScrollMode(scrollMode)
        direction := this.ApplyScrollDirection(direction)
        preserveModifiers := scrollMode != "fast" && scrollMode != "precision"
        if this.scrollConfig["smooth"] {
            this.QueueSmoothWheel(direction, speedScale, preserveModifiers)
            return
        }
        this.scrollCarry += this.scrollConfig["speed"] * speedScale
        sendCount := Floor(this.scrollCarry)
        if sendCount < 1 {
            return
        }
        this.scrollCarry -= sendCount
        this.actionManager.SendWheel(direction, sendCount, preserveModifiers)
    }

    BeginScrollMode(scrollMode) {
        if this.scrollMode = scrollMode {
            return
        }
        this.scrollMode := scrollMode
        this.scrollCarry := 0.0
        this.smoothQueue := []
        SetTimer(this.smoothTimer, 0)
    }

    QueueSmoothWheel(direction, speedScale := 1.0, preserveModifiers := true) {
        this.scrollCarry += this.scrollConfig["speed"] * speedScale
        sendCount := Floor(this.scrollCarry)
        if sendCount < 1 {
            return
        }
        this.scrollCarry -= sendCount
        if this.smoothQueue.Length > 32 {
            this.smoothQueue := []
        }
        loop sendCount {
            this.smoothQueue.Push(Map(
                "direction", direction,
                "preserveModifiers", preserveModifiers
            ))
        }
        SetTimer(this.smoothTimer, 12)
    }

    ProcessSmoothWheel(*) {
        if this.smoothQueue.Length = 0 {
            SetTimer(this.smoothTimer, 0)
            return
        }
        item := this.smoothQueue.RemoveAt(1)
        this.actionManager.SendWheel(item["direction"], 1, item["preserveModifiers"])
        if this.smoothQueue.Length = 0 {
            SetTimer(this.smoothTimer, 0)
        }
    }

    ShouldWaitForMoreClicks(buttonName) {
        return this.HasAction(buttonName, "doubleClick")
    }

    FindActiveHeldButton() {
        selectedButton := ""
        selectedSequence := -1
        for buttonName, state in this.states {
            if state["isDown"] && state["mode"] = "mapped" && state["sequence"] > selectedSequence {
                selectedButton := buttonName
                selectedSequence := state["sequence"]
            }
        }
        return selectedButton
    }

    GetAction(buttonName, triggerName) {
        if this.remaps.Has(buttonName) && this.remaps[buttonName].Has(triggerName) {
            return this.remaps[buttonName][triggerName]
        }
        return Map("type", "None", "shortcut", "")
    }

    GetWheelAction(buttonName, direction) {
        action := this.GetAction(buttonName, "holdScroll")
        return action["type"] != "None" ? action
            : this.GetAction(buttonName, direction = "up" ? "wheelUp" : "wheelDown")
    }

    GetWheelRepeatMode(action) {
        switch action["type"] {
            case "Original", "FastScroll", "PrecisionScroll", "Zoom", "VolumeUp", "VolumeDown",
                "VolumeControl", "TabNavigation", "BrowserNavigation", "DesktopSwitch":
                return "perStep"
            case "DesktopStartMenu":
                return "perDirection"
            default:
                return "once"
        }
    }

    GetDragAction(buttonName, direction) {
        action := this.GetAction(buttonName, "holdDrag")
        return action["type"] != "None" ? action
            : this.GetAction(buttonName, "drag" StrUpper(SubStr(direction, 1, 1)) SubStr(direction, 2))
    }

    HasAction(buttonName, triggerName) {
        return this.GetAction(buttonName, triggerName)["type"] != "None"
    }

    CancelAll(suppressHeld := false) {
        if !this.HasOwnProp("states") {
            return
        }
        for buttonName, state in this.states {
            SetTimer(state["clickTimer"], 0)
            SetTimer(state["holdTimer"], 0)
            state["pendingClicks"] := 0
            state["consumed"] := false
            state["gestureUsed"] := false
            state["interaction"] := "none"
            state["wheelDirections"] := Map()
            if state["isDown"] && state["mode"] = "mapped" && suppressHeld {
                state["mode"] := "suppressed"
            } else if !state["isDown"] {
                state["mode"] := "idle"
            }
        }
        this.actionManager.ReleaseAll()
    }

    CancelPending(state) {
        SetTimer(state["clickTimer"], 0)
        state["pendingClicks"] := 0
    }

    SuppressUntilRelease(state) {
        SetTimer(state["clickTimer"], 0)
        SetTimer(state["holdTimer"], 0)
        state["pendingClicks"] := 0
        state["consumed"] := false
        state["gestureUsed"] := false
        state["interaction"] := "none"
        state["wheelDirections"] := Map()
        state["mode"] := "suppressed"
        this.actionManager.ReleaseAll()
    }

    ResetPhysicalState(state) {
        state["isDown"] := false
        state["mode"] := "idle"
        state["consumed"] := false
        state["gestureUsed"] := false
        state["interaction"] := "none"
        state["wheelDirections"] := Map()
        state["pendingClicks"] := 0
    }
}
