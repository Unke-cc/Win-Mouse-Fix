#Requires AutoHotkey v2.0

class ConfigManager {
    static TriggerNames := Map(
        "click", true,
        "doubleClick", true,
        "hold", true,
        "holdScroll", true,
        "holdDrag", true,
        "dragUp", true,
        "dragDown", true,
        "dragLeft", true,
        "dragRight", true,
        "wheelUp", true,
        "wheelDown", true
    )
    static ActionTypes := Map(
        "None", true,
        "Original", true,
        "Back", true,
        "Forward", true,
        "MiddleClick", true,
        "PrimaryClick", true,
        "SecondaryClick", true,
        "TaskView", true,
        "ShowDesktop", true,
        "StartMenu", true,
        "DesktopLeft", true,
        "DesktopRight", true,
        "AltTab", true,
        "CloseWindow", true,
        "CloseTab", true,
        "NextTab", true,
        "PreviousTab", true,
        "VolumeUp", true,
        "VolumeDown", true,
        "VolumeMute", true,
        "MediaPlayPause", true,
        "CustomShortcut", true,
        "FastScroll", true,
        "Zoom", true,
        "VolumeControl", true,
        "TabNavigation", true,
        "BrowserNavigation", true,
        "DesktopSwitch", true,
        "DesktopStartMenu", true,
        "ScrollMove", true,
        "DesktopNavigation", true,
        "BrowserTabNavigation", true
    )

    __New(defaultPath, userPath) {
        this.defaultPath := defaultPath
        this.userPath := userPath
        this.activePath := ""
        this.fingerprint := ""
        this.lastError := ""
    }

    Load() {
        preferredPath := this.GetPreferredPath()
        config := 0
        this.lastError := ""

        if preferredPath != "" {
            try {
                config := Json.Parse(FileRead(preferredPath, "UTF-8"))
                this.activePath := preferredPath
            } catch Error as configError {
                this.lastError := configError.Message
            }
        }

        if !(config is Map) && preferredPath != this.defaultPath && FileExist(this.defaultPath) {
            try {
                config := Json.Parse(FileRead(this.defaultPath, "UTF-8"))
                this.activePath := this.defaultPath
            } catch Error as defaultConfigError {
                this.lastError .= (this.lastError = "" ? "" : " | ") defaultConfigError.Message
            }
        }

        if !(config is Map) {
            config := this.CreateBuiltInDefaults()
            this.activePath := ""
        }

        normalized := this.Normalize(config)
        this.fingerprint := this.GetFingerprint(this.GetPreferredPath())
        return normalized
    }

    HasChanged() {
        preferredPath := this.GetPreferredPath()
        return this.GetFingerprint(preferredPath) != this.fingerprint
    }

    GetPreferredPath() {
        if FileExist(this.userPath) {
            return this.userPath
        }
        return FileExist(this.defaultPath) ? this.defaultPath : ""
    }

    GetFingerprint(path) {
        if path = "" || !FileExist(path) {
            return "missing"
        }
        try {
            content := FileRead(path, "UTF-8")
            checksum := 0
            loop parse content {
                checksum := Mod(checksum * 131 + Ord(A_LoopField), 2147483647)
            }
            return path "|" FileGetTime(path, "M") "|" StrLen(content) "|" checksum
        } catch {
            return path "|unavailable"
        }
    }

    Normalize(source) {
        defaults := this.CreateBuiltInDefaults()
        timingSource := this.ReadMap(source, "timing", Map())
        speed := this.ReadString(source, "doubleClickSpeed", "")
        if speed != "fast" && speed != "medium" && speed != "slow" {
            oldInterval := this.ReadInteger(timingSource, "multiClickMs", 150, 150, 800)
            speed := oldInterval <= 200 ? "fast" : oldInterval <= 325 ? "medium" : "slow"
        }

        remaps := this.ReadArray(source, "remaps", 0)
        if !(remaps is Array) {
            remaps := this.ConvertLegacyButtons(this.ReadMap(source, "buttons", Map()))
        }
        if remaps.Length = 0 && !source.Has("remaps") && !source.Has("buttons") {
            remaps := defaults["remaps"]
        }

        return Map(
            "configVersion", 2,
            "enabled", this.ReadBoolean(source, "enabled", true),
            "pauseInFullscreen", this.ReadBoolean(source, "pauseInFullscreen", true),
            "doubleClickSpeed", speed,
            "desktopSwipeDirection", this.NormalizeDesktopSwipeDirection(
                this.ReadString(source, "desktopSwipeDirection", "followMouse")
            ),
            "remaps", this.NormalizeRemaps(remaps),
            "scroll", this.NormalizeScroll(this.ReadMap(source, "scroll", defaults["scroll"])),
            "excludedApps", this.NormalizeExcludedApps(this.ReadArray(source, "excludedApps", [])),
            "startup", Map("runAtLogin", this.ReadBoolean(this.ReadMap(source, "startup", Map()), "runAtLogin", false)),
            "timing", Map(
                "multiClickMs", this.DoubleClickInterval(speed),
                "holdMs", this.ReadInteger(timingSource, "holdMs", 500, 200, 2000),
                "dragThresholdPx", this.ReadInteger(timingSource, "dragThresholdPx", 12, 5, 200)
            )
        )
    }

    NormalizeRemaps(source) {
        result := []
        for item in source {
            if !(item is Map) {
                continue
            }
            button := this.NormalizeButtonName(this.ReadString(item, "button", ""))
            trigger := this.ReadString(item, "trigger", "")
            if button = "" || !ConfigManager.TriggerNames.Has(trigger) {
                continue
            }
            action := item.Has("action") ? this.NormalizeAction(item["action"], Map("type", "None", "shortcut", ""))
                : Map("type", "None", "shortcut", "")
            this.AddOrReplaceRemap(&result, Map("button", button, "trigger", trigger, "action", action))
        }
        return result
    }

    AddOrReplaceRemap(&remaps, remap) {
        for index, existing in remaps {
            if existing["button"] = remap["button"] && existing["trigger"] = remap["trigger"] {
                remaps[index] := remap
                return
            }
        }
        remaps.Push(remap)
    }

    ConvertLegacyButtons(buttons) {
        result := []
        legacyTriggers := Map(
            "click", "click",
            "doubleClick", "doubleClick",
            "hold", "hold",
            "wheelUp", "wheelUp",
            "wheelDown", "wheelDown",
            "dragUp", "dragUp",
            "dragDown", "dragDown",
            "dragLeft", "dragLeft",
            "dragRight", "dragRight"
        )
        for buttonName in ["MButton", "XButton1", "XButton2"] {
            if !buttons.Has(buttonName) || !(buttons[buttonName] is Map) {
                continue
            }
            button := buttons[buttonName]
            for oldTrigger, newTrigger in legacyTriggers {
                if !button.Has(oldTrigger) {
                    continue
                }
                action := this.NormalizeAction(button[oldTrigger], Map("type", "None", "shortcut", ""))
                if action["type"] != "None" && !this.HasTrigger(result, buttonName, newTrigger) {
                    result.Push(Map("button", buttonName, "trigger", newTrigger, "action", action))
                }
            }

        }
        return result
    }

    HasTrigger(remaps, button, trigger) {
        for item in remaps {
            if item["button"] = button && item["trigger"] = trigger {
                return true
            }
        }
        return false
    }

    NormalizeButtonName(value) {
        value := Trim(value)
        return value = "MButton" || value = "XButton1" || value = "XButton2" ? value : ""
    }

    NormalizeDesktopSwipeDirection(value) {
        return value = "oppositeMouse" ? value : "followMouse"
    }

    NormalizeAction(value, fallback) {
        if value is String {
            actionType := value
            shortcut := ""
        } else if value is Map {
            actionType := this.ReadString(value, "type", fallback["type"])
            shortcut := this.ReadString(value, "shortcut", "")
        } else {
            return this.CopyAction(fallback)
        }

        if !ConfigManager.ActionTypes.Has(actionType) {
            return this.CopyAction(fallback)
        }
        if actionType = "CustomShortcut" && Trim(shortcut) = "" {
            return Map("type", "None", "shortcut", "")
        }
        return Map("type", actionType, "shortcut", shortcut)
    }

    NormalizeScroll(source) {
        modifiers := Map(
            "horizontalModifier", this.NormalizeModifier(this.ReadString(source, "horizontalModifier", "shift"), "shift"),
            "fastModifier", this.NormalizeModifier(this.ReadString(source, "fastModifier", "alt"), "alt"),
            "precisionModifier", this.NormalizeModifier(this.ReadString(source, "precisionModifier", "win"), "win"),
            "zoomModifier", this.NormalizeModifier(this.ReadString(source, "zoomModifier", "ctrl"), "ctrl")
        )
        used := Map()
        for name in ["horizontalModifier", "fastModifier", "precisionModifier", "zoomModifier"] {
            modifier := modifiers[name]
            if modifier != "none" {
                if used.Has(modifier) {
                    modifiers[name] := "none"
                } else {
                    used[modifier] := true
                }
            }
        }
        return Map(
            "reverse", this.ReadBoolean(source, "reverse", false),
            "speed", this.ReadNumber(source, "speed", 1.0, 0.25, 4.0),
            "smooth", this.ReadBoolean(source, "smooth", false),
            "horizontalModifier", modifiers["horizontalModifier"],
            "fastModifier", modifiers["fastModifier"],
            "precisionModifier", modifiers["precisionModifier"],
            "zoomModifier", modifiers["zoomModifier"]
        )
    }

    NormalizeModifier(value, fallback) {
        value := StrLower(Trim(value))
        return value = "none" || value = "ctrl" || value = "alt" || value = "shift" || value = "win"
            ? value : fallback
    }

    NormalizeExcludedApps(source) {
        result := []
        for item in source {
            if item is String {
                result.Push(Map("name", item, "path", "", "mode", "disableAll"))
                continue
            }
            if !(item is Map) {
                continue
            }
            mode := this.ReadString(item, "mode", "disableAll")
            name := this.ReadString(item, "name", "")
            path := this.ReadString(item, "path", "")
            if mode = "disableAll" && (name != "" || path != "") {
                result.Push(Map("name", name, "path", path, "mode", mode))
            }
        }
        return result
    }

    CreateBuiltInDefaults() {
        remaps := []
        this.AddDefault(&remaps, "XButton1", "click", "Back")
        this.AddDefault(&remaps, "XButton1", "holdScroll", "DesktopStartMenu")
        this.AddDefault(&remaps, "XButton1", "holdDrag", "DesktopNavigation")
        this.AddDefault(&remaps, "XButton2", "click", "Forward")
        this.AddDefault(&remaps, "XButton2", "holdScroll", "Zoom")
        this.AddDefault(&remaps, "XButton2", "holdDrag", "ScrollMove")
        return Map(
            "configVersion", 2,
            "enabled", true,
            "pauseInFullscreen", true,
            "doubleClickSpeed", "fast",
            "desktopSwipeDirection", "followMouse",
            "remaps", remaps,
            "scroll", Map(
                "reverse", false,
                "speed", 1.0,
                "smooth", false,
                "horizontalModifier", "shift",
                "fastModifier", "alt",
                "precisionModifier", "win",
                "zoomModifier", "ctrl"
            ),
            "excludedApps", [],
            "startup", Map("runAtLogin", false),
            "timing", Map("multiClickMs", 150, "holdMs", 500, "dragThresholdPx", 12)
        )
    }

    AddDefault(&remaps, button, trigger, actionType) {
        remaps.Push(Map(
            "button", button,
            "trigger", trigger,
            "action", Map("type", actionType, "shortcut", "")
        ))
    }

    DoubleClickInterval(speed) {
        return speed = "slow" ? 400 : speed = "medium" ? 250 : 150
    }

    CopyAction(action) {
        return Map("type", action["type"], "shortcut", action["shortcut"])
    }

    ReadMap(source, key, fallback) {
        return source is Map && source.Has(key) && source[key] is Map ? source[key] : fallback
    }

    ReadArray(source, key, fallback) {
        return source is Map && source.Has(key) && source[key] is Array ? source[key] : fallback
    }

    ReadString(source, key, fallback) {
        return source is Map && source.Has(key) && source[key] is String ? source[key] : fallback
    }

    ReadBoolean(source, key, fallback) {
        if !(source is Map) || !source.Has(key) {
            return fallback
        }
        value := source[key]
        return Type(value) = "Integer" && (value = 0 || value = 1) ? !!value : fallback
    }

    ReadInteger(source, key, fallback, minimum, maximum) {
        if !(source is Map) || !source.Has(key) || Type(source[key]) != "Integer" {
            return fallback
        }
        return Max(minimum, Min(maximum, source[key]))
    }

    ReadNumber(source, key, fallback, minimum, maximum) {
        if !(source is Map) || !source.Has(key)
            || !(Type(source[key]) = "Integer" || Type(source[key]) = "Float") {
            return fallback
        }
        return Max(minimum, Min(maximum, source[key]))
    }
}
