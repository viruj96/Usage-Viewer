using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UsageViewer;

internal static class ProcessMemoryTrimmer
{
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(nint processHandle);

    internal static void TrimCurrentProcessWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        EmptyWorkingSet(process.Handle);
    }
}
