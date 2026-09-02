using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pisum.Whisper.Spikes;

/// <summary>
/// Task 5.1 (settle-win-x64-verification-debt) — drives a fatal-startup reproduction end to end:
/// launch the executable, wait for a top-level window of that process whose title matches, post
/// <c>WM_CLOSE</c> (which an <c>MB_OK</c> box answers as OK), wait for exit, and print the exit code
/// and the newest <c>[FTL]</c> line from the log.
/// </summary>
/// <remarks>
/// It is the 2026-09-02 run's script for the corrupt-settings reproduction that closed issue #20,
/// kept this time — rather than thrown away in the scratchpad — so change 12's CI can run it. The
/// state setup for each reproduction (moving a settings file aside, commenting out a registration,
/// moving a tray asset) stays by hand; this drives only the launch-observe-dismiss part every
/// reproduction shares.
/// </remarks>
internal static partial class FatalSpike
{
    private const uint WmClose = 0x0010;

    public static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("fatal is a Windows question. Nothing to run here.");
            return 0;
        }

        var exePath = args.ElementAtOrDefault(1);
        var titleSubstring = args.ElementAtOrDefault(2);

        if (exePath is null || titleSubstring is null)
        {
            Console.WriteLine("usage: spikes -- fatal <exe> <title>");
            return 2;
        }

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".pisum-whisper",
            "logs",
            "pisum-whisper.log");

        Console.WriteLine($"Launching {exePath}");

        using var process = Process.Start(exePath);
        if (process is null)
        {
            Console.WriteLine("Process.Start returned null.");
            return 1;
        }

        var window = await WaitForWindowAsync(process, titleSubstring, TimeSpan.FromSeconds(15));

        if (window is null)
        {
            Console.WriteLine(
                $"No window titled like \"{titleSubstring}\" appeared within 15 s " +
                $"(process exited: {process.HasExited}).");

            if (!process.HasExited)
            {
                process.Kill();
            }

            return 1;
        }

        Console.WriteLine($"Window found: \"{process.MainWindowTitle}\" (0x{window.Value:X})");

        PostMessageW(window.Value, WmClose, IntPtr.Zero, IntPtr.Zero);

        var exited = process.WaitForExit(TimeSpan.FromSeconds(10));
        if (!exited)
        {
            Console.WriteLine("Process did not exit within 10 s of WM_CLOSE; killing it.");
            process.Kill();
            return 1;
        }

        Console.WriteLine($"Exit code: {process.ExitCode}");

        var fatalLine = FindNewestFatalLine(logPath);
        Console.WriteLine(fatalLine is null
            ? $"No [FTL] line found in {logPath}."
            : $"Newest [FTL] line: {fatalLine}");

        return 0;
    }

    private static async Task<IntPtr?> WaitForWindowAsync(
        Process process, string titleSubstring, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();

            if (process.HasExited)
            {
                return null;
            }

            if (process.MainWindowHandle != IntPtr.Zero
                && process.MainWindowTitle.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
            {
                return process.MainWindowHandle;
            }

            await Task.Delay(200);
        }

        return null;
    }

    private static string? FindNewestFatalLine(string logPath)
    {
        return File.Exists(logPath)
            ? File.ReadLines(logPath).LastOrDefault(line => line.Contains("[FTL]"))
            : null;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
