using System.Windows;
using WinMouseFix.Gui.Models;

namespace WinMouseFix.Gui;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--validate-ui", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            try
            {
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

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
