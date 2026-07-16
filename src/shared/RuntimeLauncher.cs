using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WinMouseFix.Runtime;

public static class RuntimeLauncher
{
    public static bool EnsureTrayHost()
    {
        if (IsMutexOwned(RuntimeNames.TrayMutex))
        {
            return WaitForEvent(RuntimeNames.TrayReadyEvent);
        }

        var executable = FindExecutable("WinMouseFix.Tray.exe");
        if (executable is null)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--background",
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }

        return WaitForEvent(RuntimeNames.TrayReadyEvent);
    }

    public static bool StartSettingsProcess(bool lightweight = false)
    {
        if (Signal(RuntimeNames.SettingsActivateEvent))
        {
            return true;
        }

        var executable = FindExecutable("WinMouseFix.exe");
        if (executable is null)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = lightweight ? "--lightweight" : string.Empty,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool Signal(string eventName)
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(eventName);
            return signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    private static bool WaitForEvent(string eventName)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var ready = EventWaitHandle.OpenExisting(eventName);
                return ready.WaitOne(TimeSpan.FromMilliseconds(250));
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    public static string? FindExecutable(string fileName)
    {
        foreach (var root in CandidateRoots())
        {
            var candidates = new[]
            {
                Path.Combine(root, fileName),
                Path.Combine(root, "src", "gui", "bin", "Debug", "net48", fileName),
                Path.Combine(root, "src", "gui", "bin", "Release", "net48", fileName),
                Path.Combine(root, "src", "tray", "bin", "Debug", "net48", fileName),
                Path.Combine(root, "src", "tray", "bin", "Release", "net48", fileName)
            };

            var executable = candidates.FirstOrDefault(File.Exists);
            if (executable is not null)
            {
                return executable;
            }
        }

        return null;
    }

    public static string? FindAsset(string fileName)
    {
        foreach (var root in CandidateRoots())
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "assets", fileName),
                Path.Combine(root, "assets", fileName)
            };
            var asset = candidates.FirstOrDefault(File.Exists);
            if (asset is not null)
            {
                return asset;
            }
        }

        return null;
    }

    private static bool IsMutexOwned(string name)
    {
        try
        {
            using var mutex = Mutex.OpenExisting(name);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (var current = directory; current is not null; current = current.Parent)
        {
            if (seen.Add(current.FullName))
            {
                yield return current.FullName;
            }
        }

        if (seen.Add(Environment.CurrentDirectory))
        {
            yield return Environment.CurrentDirectory;
        }
    }
}
