using Microsoft.Win32;

namespace WinMouseFix.Gui.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinMouseFix";

    public (bool Applied, string Message) SetRunAtLogin(bool enabled)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return (false, "无法确定 Win Mouse Fix 程序路径");
        }

        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                return (false, "无法打开 Windows 登录启动设置");
            }

            if (enabled)
            {
                runKey.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
                return (true, "已设置登录 Windows 后运行");
            }

            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            return (true, "已关闭登录 Windows 后运行");
        }
        catch (UnauthorizedAccessException ex)
        {
            return (false, $"无法修改 Windows 登录启动设置：{ex.Message}");
        }
    }
}
