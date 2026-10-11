#Requires AutoHotkey v2.0

; 判断是否接管。看开关、暂停、排除、全屏。
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
        ; WinExist 失败仍接管。
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

        ; 路径优先。其次进程名。进程名也可拿来比路径。
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
            windowClass := WinGetClass("ahk_id " hwnd)
            ; 桌面与任务栏不当成全屏。
            if this.IsDesktopSurfaceClass(windowClass) {
                return false
            }
            ; 最大化或最小化则否。
            if WinGetMinMax("ahk_id " hwnd) != 0 {
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
            ; 窗体盖住显示器。容差 3 像素。
            tolerance := 3
            return windowLeft <= monitorLeft + tolerance
                && windowTop <= monitorTop + tolerance
                && windowLeft + windowWidth >= monitorRight - tolerance
                && windowTop + windowHeight >= monitorBottom - tolerance
        }
        return false
    }

    ; 类名属于桌面、任务栏、任务视图。
    IsDesktopSurfaceClass(windowClass) {
        return windowClass = "Progman"
            || windowClass = "WorkerW"
            || windowClass = "Shell_TrayWnd"
            || windowClass = "Shell_SecondaryTrayWnd"
            || windowClass = "MultitaskingViewFrame"
            || windowClass = "XamlExplorerHostIslandWindow"
    }

    PathsEqual(leftPath, rightPath) {
        leftPath := StrReplace(leftPath, "/", "\")
        rightPath := StrReplace(rightPath, "/", "\")
        return RTrim(leftPath, "\") = RTrim(rightPath, "\")
    }
}
