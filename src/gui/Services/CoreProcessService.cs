using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WinMouseFix.Gui.Services;

public sealed class CoreProcessService
{
    private const uint PauseMessage = 0x8002;
    private const uint ResumeMessage = 0x8003;
    private Process? process;

    public bool IsRunning => process is { HasExited: false };

    public event EventHandler? StatusChanged;

    public (bool Started, string Message) Start()
    {
        if (IsRunning)
        {
            return (true, "AutoHotkey 核心已经在运行");
        }

        var launch = FindLaunchTarget();
        if (launch is null)
        {
            return (false, "没有找到 AutoHotkey v2 核心。请通过开发脚本启动，或将核心放在程序目录中。");
        }

        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = launch.Value.FileName,
                Arguments = launch.Value.Arguments,
                WorkingDirectory = launch.Value.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return (false, "Windows 未能启动 AutoHotkey 核心");
            }

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return (true, "AutoHotkey 核心已启动");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            process = null;
            return (false, $"启动失败：{ex.Message}");
        }
    }

    public (bool Stopped, string Message) Stop()
    {
        if (!IsRunning)
        {
            process = null;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return (true, "AutoHotkey 核心当前未运行");
        }

        try
        {
            var engineProcess = process!;
            var closeRequested = engineProcess.CloseMainWindow();
            if (!closeRequested && !engineProcess.HasExited)
            {
                engineProcess.Kill();
            }

            if (!engineProcess.WaitForExit(1500) && !engineProcess.HasExited)
            {
                engineProcess.Kill();
                engineProcess.WaitForExit(1500);
            }

            engineProcess.Dispose();
            process = null;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return (true, "AutoHotkey 核心已停止，设置窗口保持运行");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            process = null;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return (true, "AutoHotkey 核心已停止，设置窗口保持运行");
        }
    }

    public bool Pause() => PostEngineMessage(PauseMessage);

    public bool Resume() => PostEngineMessage(ResumeMessage);

    private bool PostEngineMessage(uint message)
    {
        if (!IsRunning)
        {
            return false;
        }

        var sent = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processId == process!.Id)
            {
                var result = SendMessageTimeout(
                    window,
                    message,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0x0002,
                    250,
                    out _);
                sent |= result != IntPtr.Zero;
            }
            return true;
        }, IntPtr.Zero);
        return sent;
    }

    private static LaunchTarget? FindLaunchTarget()
    {
        foreach (var root in CandidateRoots())
        {
            var executableCandidates = new[]
            {
                Path.Combine(root, "WinMouseFix.Engine.exe"),
                Path.Combine(root, "engine", "WinMouseFix.Engine.exe"),
                Path.Combine(root, "src", "engine", "WinMouseFix.Engine.exe")
            };

            var executable = executableCandidates.FirstOrDefault(File.Exists);
            if (executable is not null)
            {
                return new LaunchTarget(executable, string.Empty, Path.GetDirectoryName(executable)!);
            }

            var scriptCandidates = new[]
            {
                Path.Combine(root, "WinMouseFix.ahk"),
                Path.Combine(root, "engine", "WinMouseFix.ahk"),
                Path.Combine(root, "src", "engine", "WinMouseFix.ahk"),
                Path.Combine(root, "src", "engine", "MouseEngine.ahk"),
                Path.Combine(root, "src", "engine", "Engine.ahk"),
                Path.Combine(root, "src", "engine", "main.ahk")
            };

            var script = scriptCandidates.FirstOrDefault(File.Exists);
            if (script is null)
            {
                continue;
            }

            var runtimeCandidates = new[]
            {
                Path.Combine(root, "AutoHotkey64.exe"),
                Path.Combine(root, "runtime", "AutoHotkey64.exe"),
                Path.Combine(root, "engine", "AutoHotkey64.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AutoHotkey", "v2", "AutoHotkey64.exe")
            };

            var runtime = runtimeCandidates.FirstOrDefault(File.Exists);
            if (runtime is not null)
            {
                return new LaunchTarget(runtime, $"\"{script}\"", Path.GetDirectoryName(script)!);
            }
        }

        return null;
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

    private readonly struct LaunchTarget
    {
        public LaunchTarget(string fileName, string arguments, string workingDirectory)
        {
            FileName = fileName;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
        }

        public string FileName { get; }
        public string Arguments { get; }
        public string WorkingDirectory { get; }
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
}
