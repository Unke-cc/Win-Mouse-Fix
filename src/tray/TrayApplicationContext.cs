using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WinMouseFix.Gui.Models;
using WinMouseFix.Gui.Services;
using WinMouseFix.Runtime;

namespace WinMouseFix.Tray;

internal sealed class TrayApplicationContext : Forms.ApplicationContext
{
    private readonly CoreProcessService coreProcessService = new();
    private readonly ProfileService profileService = new();
    private readonly TrayService trayService;
    private readonly Forms.Timer statusTimer;
    private readonly EventWaitHandle shutdownEvent;
    private readonly EventWaitHandle readyEvent;
    private readonly RegisteredWaitHandle shutdownRegistration;
    private bool exiting;
    private int shutdownRequested;

    public TrayApplicationContext()
    {
        shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RuntimeNames.TrayShutdownEvent);
        readyEvent = new EventWaitHandle(false, EventResetMode.ManualReset, RuntimeNames.TrayReadyEvent);
        readyEvent.Reset();
        shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
            shutdownEvent,
            (_, _) => Interlocked.Exchange(ref shutdownRequested, 1),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        trayService = new TrayService(LoadTrayIcon);
        trayService.OpenSettingsRequested += (_, _) => RuntimeLauncher.StartSettingsProcess();
        trayService.ToggleRequested += (_, _) => _ = ToggleEnabledAsync();
        trayService.LightweightModeRequested += (_, _) => RuntimeLauncher.Signal(RuntimeNames.SettingsLightweightEvent);
        trayService.ExitRequested += (_, _) => ExitApplication();

        statusTimer = new Forms.Timer { Interval = 1000 };
        statusTimer.Tick += (_, _) =>
        {
            if (Interlocked.Exchange(ref shutdownRequested, 0) == 1)
            {
                ExitApplication();
                return;
            }

            trayService.Update(coreProcessService.IsRunning, settingsAvailable: true);
        };
        statusTimer.Start();

        try
        {
            var config = profileService.InitializeAsync().GetAwaiter().GetResult().Config;
            if (config.Enabled)
            {
                coreProcessService.Start();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }

        trayService.Update(coreProcessService.IsRunning, settingsAvailable: true);
        readyEvent.Set();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            statusTimer.Stop();
            statusTimer.Dispose();
            shutdownRegistration.Unregister(null);
            shutdownEvent.Dispose();
            readyEvent.Reset();
            readyEvent.Dispose();
            trayService.Dispose();
            coreProcessService.Resume();
            coreProcessService.Stop();
        }

        base.Dispose(disposing);
    }

    private async Task ToggleEnabledAsync()
    {
        try
        {
            var config = (await profileService.InitializeAsync()).Config;
            var enabled = !coreProcessService.IsRunning;
            if (enabled)
            {
                coreProcessService.Start();
            }
            else
            {
                coreProcessService.Stop();
            }

            config.Enabled = coreProcessService.IsRunning;
            await profileService.SaveCurrentAsync(config);
            trayService.Update(coreProcessService.IsRunning, settingsAvailable: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            trayService.ShowBalloon("Win Mouse Fix", $"无法保存设置：{ex.Message}");
        }
    }

    private void ExitApplication()
    {
        if (exiting)
        {
            return;
        }

        exiting = true;
        RuntimeLauncher.Signal(RuntimeNames.SettingsShutdownEvent);
        ExitThread();
    }

    private static Drawing.Icon LoadTrayIcon(bool running)
    {
        var path = RuntimeLauncher.FindAsset(running ? "WinMouseFix-Tray-White.ico" : "WinMouseFix-Tray.ico")
                   ?? RuntimeLauncher.FindAsset("WinMouseFix-Tray.ico");
        if (path is not null)
        {
            using var icon = new Drawing.Icon(path);
            return (Drawing.Icon)icon.Clone();
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }
}
