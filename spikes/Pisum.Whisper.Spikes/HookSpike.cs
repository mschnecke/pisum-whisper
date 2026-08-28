using SharpHook;
using SharpHook.Data;
using SharpHook.Simulation;

namespace Pisum.Whisper.Spikes;

/// <summary>
/// S1 — does a SharpHook global hook report BOTH key edges, with the modifier mask, while another
/// application has focus? Hold-to-record is impossible if key release is not reported, so this is
/// the highest-severity question in change 1.
/// </summary>
internal static class HookSpike
{
    private static readonly KeyCode[] Combo = [KeyCode.VcLeftControl, KeyCode.VcLeftShift, KeyCode.VcSpace];

    public static async Task<int> RunAsync()
    {
        using var hook = new SimpleGlobalHook();
        using var simulator = EventSimulator.Create("Pisum Whisper Spike", SharpHook.Providers.UioHookProvider.Instance);

        var pressed = new List<string>();
        var released = new List<string>();

        hook.KeyPressed += (_, e) =>
        {
            pressed.Add($"{e.Data.KeyCode} mask={e.RawEvent.Mask} simulated={e.IsEventSimulated}");
            Console.WriteLine($"  DOWN {e.Data.KeyCode,-18} mask={e.RawEvent.Mask}");
        };
        hook.KeyReleased += (_, e) =>
        {
            released.Add($"{e.Data.KeyCode} mask={e.RawEvent.Mask} simulated={e.IsEventSimulated}");
            Console.WriteLine($"  UP   {e.Data.KeyCode,-18} mask={e.RawEvent.Mask}");
        };

        var runTask = hook.RunAsync();
        while (!hook.IsRunning) await Task.Delay(20);
        Console.WriteLine($"hook running: {hook.IsRunning}");

        // Drive the combination through the simulator. A WH_KEYBOARD_LL hook observes injected
        // events on the same path as physical ones, so this exercises the real code path; the
        // one thing it does not prove is the hardware scan-code route.
        Console.WriteLine("simulating Ctrl+Shift+Space down...");
        // A tight loop can post the next key's DOWN before macOS has folded the previous key into
        // the modifier flags, so the combo's last DOWN arrives without the earlier keys' mask set.
        foreach (var key in Combo) { simulator.SimulateKeyPress(key); await Task.Delay(30); }
        await Task.Delay(150);
        Console.WriteLine("simulating Ctrl+Shift+Space up...");
        foreach (var key in Combo.Reverse()) { simulator.SimulateKeyRelease(key); await Task.Delay(30); }
        await Task.Delay(300);

        hook.Stop();
        await runTask;

        var spaceDown = pressed.FirstOrDefault(p => p.StartsWith("VcSpace"));
        var spaceUp = released.FirstOrDefault(p => p.StartsWith("VcSpace"));

        Console.WriteLine();
        Console.WriteLine($"press events   : {pressed.Count}");
        Console.WriteLine($"release events : {released.Count}");
        Console.WriteLine($"Space DOWN     : {spaceDown ?? "*** NOT OBSERVED ***"}");
        Console.WriteLine($"Space UP       : {spaceUp ?? "*** NOT OBSERVED ***"}");

        var maskOk = spaceDown?.Contains("Ctrl") == true && spaceDown.Contains("Shift");
        Console.WriteLine($"modifier mask on Space DOWN carries Ctrl+Shift: {maskOk}");

        var verdict = spaceDown is not null && spaceUp is not null && maskOk;
        Console.WriteLine($"\nS1 VERDICT: {(verdict ? "PASS - both edges reported with modifier mask" : "FAIL")}");
        return verdict ? 0 : 1;
    }
}
