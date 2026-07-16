namespace WinMouseFix.Runtime;

public static class RuntimeNames
{
    public const string TrayMutex = @"Local\WinMouseFix.Tray.Instance";
    public const string TrayReadyEvent = @"Local\WinMouseFix.Tray.Ready";
    public const string TrayShutdownEvent = @"Local\WinMouseFix.Tray.Shutdown";
    public const string SettingsMutex = @"Local\WinMouseFix.Settings.Instance";
    public const string SettingsActivateEvent = @"Local\WinMouseFix.Settings.Activate";
    public const string SettingsLightweightEvent = @"Local\WinMouseFix.Settings.Lightweight";
    public const string SettingsShutdownEvent = @"Local\WinMouseFix.Settings.Shutdown";
}
