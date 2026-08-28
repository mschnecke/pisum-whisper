using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpHook;
using SharpHook.Data;
using SharpHook.Simulation;

namespace Pisum.Whisper.Spikes;

/// <summary>
/// S1b (task 1.3a on Windows, 1.3b on macOS) — does EventSimulator's paste shortcut actually land in
/// a FOREIGN application? Verified by round-trip: put a token on the clipboard, paste it into a
/// target editor, overwrite the clipboard with a sentinel, then select-all/copy back. If the token
/// returns, the paste landed. Overwriting with the sentinel is what stops an empty document producing
/// a false pass.
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

        try
        {
            var readBack = OperatingSystem.IsWindows()
                ? await RunWindowsAsync(simulator, token)
                : await RunMacAsync(simulator, token);
            var pass = readBack == token;

            Console.WriteLine();
            Console.WriteLine($"token written : {token}");
            Console.WriteLine($"read back     : {readBack}");
            Console.WriteLine($"hook events seen while target app had focus: {seenWhileForeign}");
            Console.WriteLine($"\nS1b VERDICT: {(pass ? "PASS - simulated paste landed in a foreign app" : "FAIL")}");
            return pass ? 0 : 1;
        }
        finally
        {
            hook.Stop();
            await runTask;
        }
    }

    private static async Task<string> RunWindowsAsync(EventSimulator simulator, string token)
    {
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

            return Clipboard.Get().Trim();
        }
        finally
        {
            try { Process.Start(new ProcessStartInfo("taskkill", $"/PID {notepad.Id} /F") { CreateNoWindow = true })!.WaitForExit(); }
            catch { /* best effort */ }
        }
    }

    private static async Task<string> RunMacAsync(EventSimulator simulator, string token)
    {
        RunOsaScript("tell application \"TextEdit\" to make new document");
        RunOsaScript("tell application \"TextEdit\" to activate");
        await Task.Delay(700);

        try
        {
            // A fresh document is already empty, but clear it in case TextEdit reused an old window.
            // The 30ms gaps matter: a tight loop can post the next key's DOWN before macOS has folded
            // the previous key into the modifier flags, so e.g. Cmd+V's V-DOWN arrives unmodified and
            // TextEdit sees a stray "v" keystroke instead of Paste.
            simulator.SimulateKeyPress(KeyCode.VcLeftMeta);
            await Task.Delay(30);
            simulator.SimulateKeyPress(KeyCode.VcA);
            await Task.Delay(30);
            simulator.SimulateKeyRelease(KeyCode.VcA);
            await Task.Delay(30);
            simulator.SimulateKeyRelease(KeyCode.VcLeftMeta);
            await Task.Delay(250);
            simulator.SimulateKeyPress(KeyCode.VcDelete);
            await Task.Delay(30);
            simulator.SimulateKeyRelease(KeyCode.VcDelete);
            await Task.Delay(250);

            MacClipboard.Set(token);
            await Task.Delay(200);

            Console.WriteLine("simulating Cmd+V into TextEdit...");
            simulator.SimulateKeyPress(KeyCode.VcLeftMeta);
            await Task.Delay(30);
            simulator.SimulateKeyPress(KeyCode.VcV);
            await Task.Delay(30);
            simulator.SimulateKeyRelease(KeyCode.VcV);
            await Task.Delay(30);
            simulator.SimulateKeyRelease(KeyCode.VcLeftMeta);
            await Task.Delay(600);

            MacClipboard.Set("SENTINEL-not-the-token");
            await Task.Delay(200);

            Console.WriteLine("selecting all and copying back out of TextEdit...");
            foreach (var key in new[] { KeyCode.VcA, KeyCode.VcC })
            {
                simulator.SimulateKeyPress(KeyCode.VcLeftMeta);
                await Task.Delay(30);
                simulator.SimulateKeyPress(key);
                await Task.Delay(30);
                simulator.SimulateKeyRelease(key);
                await Task.Delay(30);
                simulator.SimulateKeyRelease(KeyCode.VcLeftMeta);
                await Task.Delay(350);
            }

            return MacClipboard.Get().Trim();
        }
        finally
        {
            try { Process.Start(new ProcessStartInfo("killall", "-9 TextEdit") { CreateNoWindow = true })!.WaitForExit(); }
            catch { /* best effort */ }
        }
    }

    private static void RunOsaScript(string script)
    {
        var psi = new ProcessStartInfo("osascript") { CreateNoWindow = true };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
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

internal static class MacClipboard
{
    public static void Set(string text)
    {
        var psi = new ProcessStartInfo("pbcopy") { RedirectStandardInput = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(text);
        p.StandardInput.Close();
        p.WaitForExit();
    }

    public static string Get()
    {
        var psi = new ProcessStartInfo("pbpaste") { RedirectStandardOutput = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return text;
    }
}
