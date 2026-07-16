using System.Threading;
using System.Windows.Forms;
using WinMouseFix.Runtime;

namespace WinMouseFix.Tray;

internal static class TrayProgram
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var mutex = new Mutex(true, RuntimeNames.TrayMutex, out var ownsMutex);
        if (!ownsMutex)
        {
            if (args.Contains("--shutdown", StringComparer.OrdinalIgnoreCase))
            {
                RuntimeLauncher.Signal(RuntimeNames.TrayShutdownEvent);
            }

            return;
        }

        if (args.Contains("--shutdown", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        using var context = new TrayApplicationContext();
        Application.Run(context);
    }
}
