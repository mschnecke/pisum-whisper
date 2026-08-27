using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpHook;
using SharpHook.Data;
using SharpHook.Simulation;

namespace Pisum.Whisper.Spikes;

/// <summary>
/// S1b (task 1.3a) — does EventSimulator's Ctrl+V actually paste into a FOREIGN application?
/// Verified by round-trip: put a token on the clipboard, paste it into Notepad, overwrite the
/// clipboard with a sentinel, then select-all/copy back. If the token returns, the paste landed.
/// Overwriting with the sentinel is what stops an empty Notepad producing a false pass.
/// </summary>
internal static partial class PasteSpike
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    public static async Task<int> RunAsync()
    {
        var token = $"PISUM-SPIKE-{Guid.NewGuid():N}";
        using var simulator = EventSimulator.Create("Pisum Whisper Spike", SharpHook.Providers.UioHookProvider.Instance);
        using var hook = new SimpleGlobalHook();

        var seenWhileForeign = 0;
        hook.KeyPressed += (_, _) => Interlocked.Increment(ref seenWhileForeign);
        var runTask = hook.RunAsync();
        while (!hook.IsRunning) await Task.Delay(20);

        using var notepad = Process.Start("notepad.exe")!;
        for (var i = 0; i < 100 && notepad.MainWindowHandle == IntPtr.Zero; i++)
        {
            await Task.Delay(100);
            notepad.Refresh();
        }
        Console.WriteLine($"notepad window: {notepad.MainWindowHandle}");
        SetForegroundWindow(notepad.MainWindowHandle);
        await Task.Delay(700);

        try
        {
            // Windows 11 Notepad restores its previous session, so a leftover document would be
            // selected by the Ctrl+A below and concatenated into the read-back. Clear it first.
            simulator.SimulateKeyPress(KeyCode.VcLeftControl);
            simulator.SimulateKeyPress(KeyCode.VcA);
            simulator.SimulateKeyRelease(KeyCode.VcA);
            simulator.SimulateKeyRelease(KeyCode.VcLeftControl);
            await Task.Delay(250);
            simulator.SimulateKeyPress(KeyCode.VcDelete);
            simulator.SimulateKeyRelease(KeyCode.VcDelete);
            await Task.Delay(250);

            Clipboard.Set(token);
            await Task.Delay(200);

            Console.WriteLine("simulating Ctrl+V into Notepad...");
            simulator.SimulateKeyPress(KeyCode.VcLeftControl);
            simulator.SimulateKeyPress(KeyCode.VcV);
            simulator.SimulateKeyRelease(KeyCode.VcV);
            simulator.SimulateKeyRelease(KeyCode.VcLeftControl);
            await Task.Delay(600);

            Clipboard.Set("SENTINEL-not-the-token");
            await Task.Delay(200);

            Console.WriteLine("selecting all and copying back out of Notepad...");
            foreach (var key in new[] { KeyCode.VcA, KeyCode.VcC })
            {
                simulator.SimulateKeyPress(KeyCode.VcLeftControl);
                simulator.SimulateKeyPress(key);
                simulator.SimulateKeyRelease(key);
                simulator.SimulateKeyRelease(KeyCode.VcLeftControl);
                await Task.Delay(350);
            }

            var readBack = Clipboard.Get().Trim();
            var pass = readBack == token;

            Console.WriteLine();
            Console.WriteLine($"token written : {token}");
            Console.WriteLine($"read back     : {readBack}");
            Console.WriteLine($"hook events seen while Notepad had focus: {seenWhileForeign}");
            Console.WriteLine($"\nS1b VERDICT: {(pass ? "PASS - simulated paste landed in a foreign app" : "FAIL")}");
            return pass ? 0 : 1;
        }
        finally
        {
            hook.Stop();
            await runTask;
            try { Process.Start(new ProcessStartInfo("taskkill", $"/PID {notepad.Id} /F") { CreateNoWindow = true })!.WaitForExit(); }
            catch { /* best effort */ }
        }
    }
}

internal static class Clipboard
{
    public static void Set(string text)
    {
        var psi = new ProcessStartInfo("powershell", "-NoProfile -Command \"$input | Set-Clipboard\"")
        { RedirectStandardInput = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(text);
        p.StandardInput.Close();
        p.WaitForExit();
    }

    public static string Get()
    {
        var psi = new ProcessStartInfo("powershell", "-NoProfile -Command \"Get-Clipboard -Raw\"")
        { RedirectStandardOutput = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return text;
    }
}
