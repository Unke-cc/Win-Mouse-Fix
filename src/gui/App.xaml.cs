using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WinMouseFix.Gui.Models;
using WinMouseFix.Gui.Services;
using WinMouseFix.Runtime;

namespace WinMouseFix.Gui;

public partial class App : System.Windows.Application
{
    private Mutex? instanceMutex;
    private EventWaitHandle? activationEvent;
    private EventWaitHandle? lightweightEvent;
    private EventWaitHandle? shutdownEvent;
    private RegisteredWaitHandle? activationRegistration;
    private RegisteredWaitHandle? lightweightRegistration;
    private RegisteredWaitHandle? shutdownRegistration;
    private bool ownsInstanceMutex;
    private readonly CoreProcessService coreProcessService = new();
    private readonly ProfileService profileService = new();
    private DispatcherTimer? lightweightModeTimer;
    private MainWindow? mainWindow;
    private bool exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--validate-ui", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            try
            {
                if (!WinMouseFix.Gui.MainWindow.TryParseReleaseVersion("v1.2.3", out var version) ||
                    !version.Equals(new Version(1, 2, 3)) ||
                    WinMouseFix.Gui.MainWindow.TryParseReleaseVersion("latest", out _))
                {
                    throw new InvalidOperationException("Update version parsing failed.");
                }

                var optionsWindow = new ButtonOptionsWindow(new AppConfig());
                optionsWindow.Close();
                var profileWindow = new ProfileManagerWindow(
                    new Services.ProfileService(Path.Combine(Path.GetTempPath(), "winmousefix-ui-check")),
                    new AppConfig());
                profileWindow.Close();
                var defaultsWindow = new DefaultPresetWindow(Services.MouseDeviceType.FiveOrMore);
                defaultsWindow.Close();
                Shutdown(0);
            }
            catch
            {
                Shutdown(1);
            }

            return;
        }

        var startInBackground = e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        var startLightweight = e.Args.Contains("--lightweight", StringComparer.OrdinalIgnoreCase);
        var shutdownRequested = e.Args.Contains("--shutdown", StringComparer.OrdinalIgnoreCase);
        activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RuntimeNames.SettingsActivateEvent);
        lightweightEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RuntimeNames.SettingsLightweightEvent);
        shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RuntimeNames.SettingsShutdownEvent);
        instanceMutex = new Mutex(true, RuntimeNames.SettingsMutex, out ownsInstanceMutex);
        if (!ownsInstanceMutex)
        {
            if (shutdownRequested)
            {
                shutdownEvent.Set();
                RuntimeLauncher.Signal(RuntimeNames.TrayShutdownEvent);
            }
            else if (startLightweight)
            {
                lightweightEvent.Set();
            }
            else if (!startInBackground)
            {
                activationEvent.Set();
            }

            Shutdown(0);
            return;
        }

        if (shutdownRequested)
        {
            RuntimeLauncher.Signal(RuntimeNames.TrayShutdownEvent);
            Shutdown(0);
            return;
        }

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        RuntimeLauncher.EnsureTrayHost();
        coreProcessService.StatusChanged += CoreProcessService_StatusChanged;

        lightweightModeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        lightweightModeTimer.Tick += LightweightModeTimer_Tick;

        CreateMainWindow(startInBackground);

        activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke(OpenSettingsWindow);
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        if (startLightweight)
        {
            Dispatcher.BeginInvoke(new Action(() => _ = EnterLightweightModeAsync()));
        }
        lightweightRegistration = ThreadPool.RegisterWaitForSingleObject(
            lightweightEvent,
            (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke(() => _ = EnterLightweightModeAsync());
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
            shutdownEvent,
            (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke(() => _ = ExitApplicationAsync());
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private void CreateMainWindow(bool startHidden)
    {
        mainWindow = new MainWindow(coreProcessService, profileService)
        {
            StartHidden = startHidden
        };
        MainWindow = mainWindow;
        mainWindow.WindowHiddenToTray += MainWindow_WindowHiddenToTray;
        mainWindow.ActivityDetected += MainWindow_ActivityDetected;
        if (startHidden)
        {
            mainWindow.Opacity = 0;
            mainWindow.ShowActivated = false;
            mainWindow.ShowInTaskbar = false;
        }

        mainWindow.Show();
        ResetLightweightModeTimer();
    }

    private void MainWindow_WindowHiddenToTray(object? sender, EventArgs e)
    {
        if (exiting || lightweightModeTimer is null)
        {
            return;
        }

        ResetLightweightModeTimer();
    }

    private void MainWindow_ActivityDetected(object? sender, EventArgs e)
    {
        if (!exiting && mainWindow?.IsVisible == true)
        {
            ResetLightweightModeTimer();
        }
    }

    private void ResetLightweightModeTimer()
    {
        if (exiting || lightweightModeTimer is null)
        {
            return;
        }

        lightweightModeTimer.Stop();
        lightweightModeTimer.Start();
    }

    private void OpenSettingsWindow()
    {
        if (mainWindow is null)
        {
            CreateMainWindow(startHidden: false);
        }
        else
        {
            mainWindow.ShowSettingsWindow();
        }

        ResetLightweightModeTimer();
    }

    private async void LightweightModeTimer_Tick(object? sender, EventArgs e)
    {
        lightweightModeTimer?.Stop();
        await EnterLightweightModeAsync();
    }

    private async Task EnterLightweightModeAsync()
    {
        var window = mainWindow;
        if (window is null)
        {
            return;
        }

        if (!await window.EnterLightweightModeAsync())
        {
            return;
        }

        window.WindowHiddenToTray -= MainWindow_WindowHiddenToTray;
        window.ActivityDetected -= MainWindow_ActivityDetected;
        mainWindow = null;
        MainWindow = null;
        Shutdown(0);
    }

    private void CoreProcessService_StatusChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            Dispatcher.BeginInvoke(() =>
            {
                mainWindow?.SyncRunningState();
            });
        }
    }

    private async Task ExitApplicationAsync()
    {
        if (exiting)
        {
            return;
        }

        exiting = true;
        lightweightModeTimer?.Stop();
        if (mainWindow is not null)
        {
            await mainWindow.PrepareForApplicationShutdownAsync();
            mainWindow.WindowHiddenToTray -= MainWindow_WindowHiddenToTray;
            mainWindow.ActivityDetected -= MainWindow_ActivityDetected;
            mainWindow = null;
            MainWindow = null;
        }

        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        lightweightModeTimer?.Stop();
        activationRegistration?.Unregister(null);
        lightweightRegistration?.Unregister(null);
        shutdownRegistration?.Unregister(null);
        coreProcessService.StatusChanged -= CoreProcessService_StatusChanged;
        activationEvent?.Dispose();
        lightweightEvent?.Dispose();
        shutdownEvent?.Dispose();
        if (ownsInstanceMutex)
        {
            instanceMutex?.ReleaseMutex();
        }
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
