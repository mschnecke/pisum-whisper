namespace Pisum.Whisper.Platform.Output;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pisum.Whisper.Core.Output;

/// <summary>
/// Whether the window that holds focus sits at an integrity level this process can reach with
/// <c>SendInput</c>.
/// </summary>
/// <remarks>
/// A heuristic, and known to be the weaker of the two probes: a protected process can deny us for
/// reasons unrelated to integrity. The error direction is the safe one, though — a false negative
/// costs the user a manual Ctrl+V with their transcript intact, where a false positive is the silent
/// loss the probe exists to prevent — so anything other than an outright access denial is answered
/// as reachable.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsPasteProbe : IPasteProbe
{
    private const uint QueryLimitedInformation = 0x1000;

    private const int AccessDenied = 5;

    public bool CanPaste()
    {
        var window = GetForegroundWindow();

        if (window == IntPtr.Zero)
        {
            // Nothing holds focus. The paste has nowhere to land, but that is not this probe's
            // question, and refusing here would degrade a delivery the user may still be able to use.
            return true;
        }

        _ = GetWindowThreadProcessId(window, out var processId);

        if (processId == 0)
        {
            return true;
        }

        var process = OpenProcess(QueryLimitedInformation, false, processId);

        if (process != IntPtr.Zero)
        {
            CloseHandle(process);
            return true;
        }

        return Marshal.GetLastPInvokeError() != AccessDenied;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
