#Requires AutoHotkey v2.0

class AppRuleManager {
    __New(config) {
        this.userPaused := false
        this.SetConfig(config)
    }

    SetConfig(config) {
        this.enabled := config["enabled"]
        this.pauseInFullscreen := config["pauseInFullscreen"]
        this.excludedApps := config["excludedApps"]
    }

    SetPaused(paused) {
        this.userPaused := !!paused
    }

    ShouldHandle() {
        if !this.enabled || this.userPaused {
            return false
        }

        hwnd := WinExist("A")
        if !hwnd {
            return true
        }
        if this.IsExcludedWindow(hwnd) {
            return false
        }
        if this.pauseInFullscreen && this.IsFullscreenWindow(hwnd) {
            return false
        }
        return true
    }

    IsExcludedWindow(hwnd) {
        processName := ""
        processPath := ""
        try processName := StrLower(WinGetProcessName("ahk_id " hwnd))
        try processPath := StrLower(WinGetProcessPath("ahk_id " hwnd))

        for rule in this.excludedApps {
            ruleName := StrLower(Trim(rule["name"]))
            rulePath := StrLower(Trim(rule["path"]))
            if rulePath != "" && processPath != "" && this.PathsEqual(processPath, rulePath) {
                return true
            }
            if ruleName != "" && processName = ruleName {
                return true
            }
            if ruleName != "" && processPath != "" && this.PathsEqual(processPath, ruleName) {
                return true
            }
        }
        return false
    }

    IsFullscreenWindow(hwnd) {
        try {
            if WinGetMinMax("ahk_id " hwnd) = -1 {
                return false
            }
            WinGetPos(&windowLeft, &windowTop, &windowWidth, &windowHeight, "ahk_id " hwnd)
        } catch {
            return false
        }

        if windowWidth <= 0 || windowHeight <= 0 {
            return false
        }
        centerX := windowLeft + windowWidth / 2
        centerY := windowTop + windowHeight / 2
        monitorCount := MonitorGetCount()
        loop monitorCount {
            MonitorGet(A_Index, &monitorLeft, &monitorTop, &monitorRight, &monitorBottom)
            if centerX < monitorLeft || centerX >= monitorRight
                || centerY < monitorTop || centerY >= monitorBottom {
                continue
            }
            tolerance := 3
            return windowLeft <= monitorLeft + tolerance
                && windowTop <= monitorTop + tolerance
                && windowLeft + windowWidth >= monitorRight - tolerance
                && windowTop + windowHeight >= monitorBottom - tolerance
        }
        return false
    }

    PathsEqual(leftPath, rightPath) {
        leftPath := StrReplace(leftPath, "/", "\")
        rightPath := StrReplace(rightPath, "/", "\")
        return RTrim(leftPath, "\") = RTrim(rightPath, "\")
    }
}
