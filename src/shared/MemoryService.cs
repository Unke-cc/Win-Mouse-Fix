using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMouseFix.Shared;

/// <summary>
/// 内存管理服务：提供工作集主动修剪能力，降低常驻后台运行时的内存占用。
/// </summary>
public static class MemoryService
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    /// <summary>
    /// 在窗口隐藏或最小化至托盘时主动回收无用对象并修剪工作集，将内存回落至 3~5 MB 级别。
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                using var currentProcess = Process.GetCurrentProcess();
                SetProcessWorkingSetSize(currentProcess.Handle, new IntPtr(-1), new IntPtr(-1));
            }
        }
        catch
        {
            // 内存修剪属于非关键性能优化，异常时不影响程序正常逻辑
        }
    }
}
