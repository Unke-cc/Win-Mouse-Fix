#Requires AutoHotkey v2.0

; 注册热键。跟踪按下、点击、长按、拖动、滚轮。
class MouseManager {
    __New(config, appRuleManager, actionManager) {
        this.appRuleManager := appRuleManager
        this.actionManager := actionManager
        this.states := Map()
        this.registeredButtons := []
        this.started := false
        ; sequence 用来比较谁后按下。
        this.sequence := 0
        ; scrollCarry 累积不足一格。
        this.scrollCarry := 0.0
        this.scrollMode := "regular"
        this.smoothQueue := []
        this.smoothTimer := ObjBindMethod(this, "ProcessSmoothWheel")
        this.inertiaVelocity := 0.0
        this.inertiaCarry := 0.0
        this.inertiaLastTick := 0
        this.inertiaLastDir := ""
        this.inertiaFlingCount := 0
        this.inertiaRecentIntervals := []
        this.inertiaTimer := ObjBindMethod(this, "ProcessInertiaWheel")
        this.motionTimer := ObjBindMethod(this, "CheckMotionAndContext")
        this.lastCanHandle := appRuleManager.ShouldHandle()
        this.wheelHookActive := false
        this.SetConfig(config)
    }

    ; 先卸热键并取消动作，再按配置重注册。
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
        this.ClearInertia()
        this.wheelHookActive := false
        if wasStarted {
            this.RegisterHotkeys()
        }
    }

    ; 按按钮与触发器索引动作。
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
            ; 动作全是 None 则不注册。
            if !hasAction {
                continue
            }
            ; mode: idle mapped native suppressed。
            ; consumed：动作已完成，松手不再点。
            ; interaction: none hold drag wheel。
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
            ; $ 防止 Send 重入。* 有修饰键也触发。
            try {
                Hotkey("$*" buttonName, ObjBindMethod(this, "OnButtonDown", buttonName), "On")
                Hotkey("$*" buttonName " Up", ObjBindMethod(this, "OnButtonUp", buttonName), "On")
                this.registeredButtons.Push(buttonName)
            } catch Error as registrationError {
                try Hotkey("$*" buttonName, "Off")
                ; 设备缺键则忽略。
                FileAppend("Ignoring unsupported input " buttonName ": " registrationError.Message "`n", "**")
            }
        }
        this.wheelHookActive := false
        this.EnsureWheelHotkeys()
        ; 按需启动位移检测，避免日常空转。
        this.EnsureMotionTimer()
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
        this.smoothQueue := []
        this.ClearInertia()
        for buttonName in this.registeredButtons {
            try Hotkey("$*" buttonName, "Off")
            try Hotkey("$*" buttonName " Up", "Off")
        }
        this.registeredButtons := []
        this.SetWheelHotkeys(false)
        this.EnsureMotionTimer()
    }

    ; 【核心安全铁律】：主鼠标按键（LButton 左键、RButton 右键）绝对不可作为手势键或功能键进行全局拦截，
    ; 仅允许 XButton1、XButton2、MButton 等辅助键参与手势映射，确保 Windows 系统基本操作的绝对可用性。

    ; 按需控制位移检测定时器：
    ; 仅在有按键处于按下且映射状态时以 15ms 高频运行检测拖拽手势；
    ; 日常无按键按下时彻底休眠（0ms），消灭每秒 66 次的后台空转轮询，大幅减少 CPU 占用。
    EnsureMotionTimer() {
        if !this.started {
            SetTimer(this.motionTimer, 0)
            return
        }
        if this.HasActiveMappedButton() {
            SetTimer(this.motionTimer, 15)
        } else {
            SetTimer(this.motionTimer, 0)
        }
    }

    ; 动态控制滚轮热键挂载。
    ; 开启 takeOverWheel 时常驻挂载；
    ; 关闭 takeOverWheel 时仅在有映射按键处于按下状态时动态挂载，平时完全放行触控板与原生滚轮。
    EnsureWheelHotkeys() {
        if !this.started {
            this.SetWheelHotkeys(false)
            return
        }
        shouldEnable := this.scrollConfig["takeOverWheel"] || this.HasActiveMappedButton()
        this.SetWheelHotkeys(shouldEnable)
    }

    SetWheelHotkeys(enable) {
        if enable {
            if !this.wheelHookActive {
                try {
                    Hotkey("$*WheelUp", ObjBindMethod(this, "OnWheel", "up"), "On")
                    Hotkey("$*WheelDown", ObjBindMethod(this, "OnWheel", "down"), "On")
                    this.wheelHookActive := true
                }
            }
        } else {
            if this.wheelHookActive {
                try {
                    Hotkey("$*WheelUp", "Off")
                    Hotkey("$*WheelDown", "Off")
                    this.wheelHookActive := false
                }
            }
        }
    }

    HasActiveMappedButton() {
        for _, state in this.states {
            if state["isDown"] && state["mode"] = "mapped" {
                return true
            }
        }
        return false
    }

    OnButtonDown(buttonName, *) {
        state := this.states[buttonName]
        ; 重复按下消息忽略。
        if state["isDown"] {
            return
        }

        ; 不接管。记下 native，原样按下。
        if !this.appRuleManager.ShouldHandle() {
            this.CancelPending(state)
            state["isDown"] := true
            state["mode"] := "native"
            this.actionManager.SendButtonDown(buttonName)
            return
        }

        ; pendingClicks 未清。先停结算。
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
            ; 间隔为负，只触发一次。
            SetTimer(state["holdTimer"], -this.holdMs)
        }
        this.EnsureWheelHotkeys()
        this.EnsureMotionTimer()
    }

    OnButtonUp(buttonName, *) {
        state := this.states[buttonName]
        if !state["isDown"] {
            return
        }

        SetTimer(state["holdTimer"], 0)
        ; 补发弹起，与按下成对。
        if state["mode"] = "native" {
            this.actionManager.SendButtonUp(buttonName)
            this.ResetPhysicalState(state)
            return
        }
        ; 暂停或排除期间按下，松手也不映射。
        if state["mode"] = "suppressed" {
            this.ResetPhysicalState(state)
            return
        }

        state["isDown"] := false
        state["mode"] := "idle"
        this.EnsureWheelHotkeys()
        this.EnsureMotionTimer()
        ; 长按、拖、滚已发生，不再当点击。
        if state["consumed"] || state["gestureUsed"] {
            state["consumed"] := false
            state["gestureUsed"] := false
            this.CancelPending(state)
            return
        }

        state["pendingClicks"] += 1
        ; 有 doubleClick 才等待。否则立刻结算。
        if state["pendingClicks"] >= 2 || !this.ShouldWaitForMoreClicks(buttonName) {
            this.FinalizeClicks(buttonName)
        } else {
            SetTimer(state["clickTimer"], -this.multiClickMs)
        }
    }

    ; 仍按下、仍 mapped、无手势，才执行 hold。
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

        ; None 当 Original，避免单击被吞。
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

    ; 状态仍按下，但物理已松开。
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
            ; 按键状态丢失：若曾走 native 则补弹起。
            if state["mode"] = "native" {
                this.actionManager.SendButtonUp(buttonName)
            }
            this.ResetPhysicalState(state)
        }

        canHandle := this.appRuleManager.ShouldHandle()
        ; 刚变得不能接管：按住改为 suppressed。
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
            ; 第一次用配置阈值。连续拖降到 8 像素。
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
                ; ScrollMove 跟手移动。ScrollMove 以外只触发一次。
                if action["type"] != "ScrollMove" {
                    state["consumed"] := true
                }
            }
        }
    }

    OnWheel(direction, *) {
        ; 手势一律由已按下的鼠标按钮（侧键/中键）判定，彻底消除键盘修饰键冲突
        activeButton := this.FindActiveHeldButton()
        if activeButton != "" {
            state := this.states[activeButton]
            ; 按住中失去接管。滚轮交回系统。
            if !this.appRuleManager.ShouldHandle() {
                this.SuppressUntilRelease(state)
                this.actionManager.SendWheel(direction)
                return
            }
            ; 手势已定，滚轮不再改写。
            if state["interaction"] = "hold" || state["interaction"] = "drag" {
                if this.scrollConfig["takeOverWheel"] {
                    this.SendRegularWheel(direction, 1.0, "regular", false)
                } else {
                    this.actionManager.SendWheel(direction)
                }
                return
            }
            action := this.GetWheelAction(activeButton, direction)
            if action["type"] = "None" {
                ; 已进入 holdScroll。方向无动作则忽略。
                if state["interaction"] = "wheel" {
                    return
                }
            } else {
                ; once：按住期间只执行一次。perDirection：每个方向一次。
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
        ; 普通滚动：完全放行系统原生修饰键（如 Ctrl 缩放、Shift 横向卷动）。
        ; 仅当开启了平滑或惯性等物理调优时才接管速度；否则 100% 交还系统原生。
        if this.scrollConfig["takeOverWheel"] {
            this.SendRegularWheel(direction)
        } else {
            this.actionManager.SendWheel(direction)
        }
    }

    TryModifierWheel(direction, mouseOnly := false) {
        matches := [
            Map("modifier", this.scrollConfig["horizontalModifier"], "action", "horizontal"),
            Map("modifier", this.scrollConfig["fastModifier"], "action", "fast"),
            Map("modifier", this.scrollConfig["precisionModifier"], "action", "precision"),
            Map("modifier", this.scrollConfig["zoomModifier"], "action", "zoom")
        ]
        ; 同时命中多组，比令牌数量。多则胜。
        ; mouseOnly 为真则必须含中键或侧键。
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

    ; FastScroll、PrecisionScroll 在此消化。其余交给 Execute。
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

    ; reverse 为真则上下对调。
    ApplyScrollDirection(direction) {
        return this.scrollConfig["reverse"] ? (direction = "up" ? "down" : "up") : direction
    }

    SendRegularWheel(direction, speedScale := 1.0, scrollMode := "regular", allowInertia := true) {
        ; 不能接管时原样转发。
        if !this.appRuleManager.ShouldHandle() {
            this.actionManager.SendWheel(direction)
            return
        }
        this.BeginScrollMode(scrollMode)
        direction := this.ApplyScrollDirection(direction)
        ; fast、precision 去掉 Blind，避免 Alt/Win 触发手势。
        preserveModifiers := scrollMode != "fast" && scrollMode != "precision"
        ; 惯性只给 regular 且 speedScale=1。优先于 smooth。
        if allowInertia && scrollMode = "regular" && speedScale = 1.0 && this.scrollConfig["inertia"] {
            this.QueueInertiaWheel(direction)
            return
        }
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

    ; 惯性飞轮物理模型：严格根据拨动脉冲密度判定甩轮意图。日常单格慢读绝不触发；仅在极速猛刷时才激活短促滑行
    QueueInertiaWheel(direction) {
        now := A_TickCount
        interval := now - this.inertiaLastTick
        this.inertiaLastTick := now

        ; 先立即放行当前单格滚轮，确保首触 0 延迟
        this.actionManager.SendWheel(direction, 1, true)

        ; 反向或停顿超过 160ms：判定为重新开始慢速阅读，重置飞轮计数与动量
        if (direction != this.inertiaLastDir || interval > 160) {
            this.inertiaFlingCount := 1
            this.inertiaLastDir := direction
            this.inertiaRecentIntervals := [interval]
            this.ClearInertia()
            return
        }

        this.inertiaRecentIntervals.Push(interval)
        if (this.inertiaRecentIntervals.Length > 3) {
            this.inertiaRecentIntervals.RemoveAt(1)
        }

        this.inertiaFlingCount += 1

        ; 计算最近脉冲的平均间隔
        totalInterval := 0
        for item in this.inertiaRecentIntervals {
            totalInterval += item
        }
        avgInterval := totalInterval / this.inertiaRecentIntervals.Length

        ; 【严格飞轮门槛】：必须同向连续拨动满 4 格，且平均间隔小于 60 毫秒（极速猛刷），才注入飞轮滑行动量
        if (this.inertiaFlingCount >= 4 && avgInterval < 60) {
            impulse := this.scrollConfig["speed"] * 1.0
            this.inertiaVelocity += direction = "up" ? impulse : -impulse
            this.inertiaVelocity := Max(-10.0, Min(10.0, this.inertiaVelocity))
            SetTimer(this.inertiaTimer, 16)
        }
    }

    ; 惯性步进处理：以 0.72 阻尼快速收敛，低于 0.25 立即干脆刹车，动作紧实不飘移
    ProcessInertiaWheel(*) {
        try {
            if !this.started || !this.appRuleManager.ShouldHandle() || !this.scrollConfig["takeOverWheel"] {
                this.ClearInertia()
                return
            }
            if Abs(this.inertiaVelocity) < 0.25 {
                this.ClearInertia()
                return
            }
            magnitude := Abs(this.inertiaVelocity) + this.inertiaCarry
            sendCount := Floor(magnitude)
            this.inertiaCarry := magnitude - sendCount
            if sendCount > 0 {
                this.actionManager.SendWheel(this.inertiaVelocity > 0 ? "up" : "down", Min(sendCount, 4))
            }
            this.inertiaVelocity *= 0.72
        } catch {
            this.ClearInertia()
        }
    }

    ClearInertia() {
        this.inertiaVelocity := 0.0
        this.inertiaCarry := 0.0
        this.inertiaFlingCount := 0
        this.inertiaRecentIntervals := []
        this.inertiaLastTick := 0
        this.inertiaLastDir := ""
        if this.HasOwnProp("inertiaTimer") {
            SetTimer(this.inertiaTimer, 0)
        }
    }

    ; 换模式时清累积、队列、惯性。
    BeginScrollMode(scrollMode) {
        if this.scrollMode = scrollMode {
            return
        }
        this.scrollMode := scrollMode
        this.scrollCarry := 0.0
        this.smoothQueue := []
        SetTimer(this.smoothTimer, 0)
        this.ClearInertia()
    }

    QueueSmoothWheel(direction, speedScale := 1.0, preserveModifiers := true) {
        try {
            this.scrollCarry += this.scrollConfig["speed"] * speedScale
            sendCount := Floor(this.scrollCarry)
            if sendCount < 1 {
                return
            }
            this.scrollCarry -= sendCount
            ; 累积满 1 格入队。超过 32 项则丢队列。12 毫秒发一格。
            if this.smoothQueue.Length > 32 {
                this.smoothQueue := []
            }
            loop Min(sendCount, 16) {
                this.smoothQueue.Push(Map(
                    "direction", direction,
                    "preserveModifiers", preserveModifiers
                ))
            }
            SetTimer(this.smoothTimer, 12)
        }
    }

    ProcessSmoothWheel(*) {
        try {
            if this.smoothQueue.Length = 0 {
                SetTimer(this.smoothTimer, 0)
                return
            }
            item := this.smoothQueue.RemoveAt(1)
            this.actionManager.SendWheel(item["direction"], 1, item["preserveModifiers"])
            if this.smoothQueue.Length = 0 {
                SetTimer(this.smoothTimer, 0)
            }
        } catch {
            SetTimer(this.smoothTimer, 0)
        }
    }

    ShouldWaitForMoreClicks(buttonName) {
        return this.HasAction(buttonName, "doubleClick")
    }

    ; mapped 按下不止一个时，sequence 更大则优先。
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

    ; holdScroll 优先于 wheelUp、wheelDown。
    GetWheelAction(buttonName, direction) {
        action := this.GetAction(buttonName, "holdScroll")
        return action["type"] != "None" ? action
            : this.GetAction(buttonName, direction = "up" ? "wheelUp" : "wheelDown")
    }

    ; Original FastScroll 等逐步重复。DesktopStartMenu 分方向。否则 once。
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

    ; holdDrag 优先于 dragUp 等四向。
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
            ; suppressHeld 为真：mapped 且仍按下，改成 suppressed。
            if state["isDown"] && state["mode"] = "mapped" && suppressHeld {
                state["mode"] := "suppressed"
            } else if !state["isDown"] {
                state["mode"] := "idle"
            }
        }
        this.EnsureWheelHotkeys()
        this.EnsureMotionTimer()
        this.actionManager.ReleaseAll()
    }

    CancelPending(state) {
        SetTimer(state["clickTimer"], 0)
        state["pendingClicks"] := 0
    }

    ; 松手之前不再触发映射。
    SuppressUntilRelease(state) {
        SetTimer(state["clickTimer"], 0)
        SetTimer(state["holdTimer"], 0)
        state["pendingClicks"] := 0
        state["consumed"] := false
        state["gestureUsed"] := false
        state["interaction"] := "none"
        state["wheelDirections"] := Map()
        state["mode"] := "suppressed"
        this.EnsureWheelHotkeys()
        this.EnsureMotionTimer()
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
        this.EnsureWheelHotkeys()
        this.EnsureMotionTimer()
    }
}
