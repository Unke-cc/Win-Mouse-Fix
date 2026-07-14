using System.Threading;
using System.Windows;
using WinMouseFix.Gui.Models;

namespace WinMouseFix.Gui;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\WinMouseFix.Gui.Instance";
    private const string ActivationEventName = @"Local\WinMouseFix.Gui.Activate";
    private Mutex? instanceMutex;
    private EventWaitHandle? activationEvent;
    private RegisteredWaitHandle? activationRegistration;
    private bool ownsInstanceMutex;

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

                using var trayIcon = WinMouseFix.Gui.MainWindow.LoadTrayIcon();

                var optionsWindow = new ButtonOptionsWindow(new AppConfig());
                optionsWindow.Close();
                Shutdown(0);
            }
            catch
            {
                Shutdown(1);
            }

            return;
        }

        var startInBackground = e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        instanceMutex = new Mutex(true, InstanceMutexName, out ownsInstanceMutex);
        if (!ownsInstanceMutex)
        {
            if (!startInBackground)
            {
                activationEvent.Set();
            }

            Shutdown(0);
            return;
        }

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.StartHidden = startInBackground;
        if (startInBackground)
        {
            mainWindow.Opacity = 0;
            mainWindow.ShowActivated = false;
            mainWindow.ShowInTaskbar = false;
        }

        activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke(mainWindow.ShowSettingsWindow);
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        activationRegistration?.Unregister(null);
        activationEvent?.Dispose();
        if (ownsInstanceMutex)
        {
            instanceMutex?.ReleaseMutex();
        }
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
