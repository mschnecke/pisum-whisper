using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Data;
using SharpHook.Simulation;

namespace Pisum.Whisper.Spikes;

/// <summary>
/// Task 1.9's companion — the co-existence question: can a SharpHook global hook run at the same
/// time as Avalonia's run loop? This is the shape the real app takes (Avalonia owns the main
/// thread, the hook owns a background thread with its own loop).
///
/// On macOS this is the highest-severity unknown in the project, because libuiohook calls
/// CFRunLoopGetCurrent on the thread that runs it. THIS SPIKE IS THE ONE TO RUN FIRST ON A MAC.
/// </summary>
internal static class CombinedSpike
{
    public static Task<int> RunAsync()
    {
        var exit = AppBuilder.Configure<CombinedApp>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
        return Task.FromResult(exit);
    }
}

internal sealed class CombinedApp : Application
{
    private static readonly string IdleIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-idle.png");
    private static readonly string RecordingIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-recording.png");

    private readonly SimpleGlobalHook _hook = new();
    private TrayIcon? _tray;
    private int _presses;
    private int _releases;
    private int _uiThreadUpdates;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => desktop.Shutdown();
        var menu = new NativeMenu();
        menu.Items.Add(quit);

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(IdleIcon),
            ToolTipText = "Pisum Whisper - combined spike",
            Menu = menu,
            IsVisible = true,
        };

        // Hook events arrive on the hook's own thread. Marshalling to the UI thread here is exactly
        // what change 9 must do to drive the icon from the recording state.
        _hook.KeyPressed += (_, e) =>
        {
            Interlocked.Increment(ref _presses);
            if (e.Data.KeyCode != KeyCode.VcSpace) return;
            Dispatcher.UIThread.Post(() =>
            {
                _uiThreadUpdates++;
                _tray!.Icon = new WindowIcon(RecordingIcon);
                Console.WriteLine($"  [ui thread] icon -> recording (hook thread reported DOWN, mask={e.RawEvent.Mask})");
            });
        };
        _hook.KeyReleased += (_, e) =>
        {
            Interlocked.Increment(ref _releases);
            if (e.Data.KeyCode != KeyCode.VcSpace) return;
            Dispatcher.UIThread.Post(() =>
            {
                _uiThreadUpdates++;
                _tray!.Icon = new WindowIcon(IdleIcon);
                Console.WriteLine("  [ui thread] icon -> idle   (hook thread reported UP)");
            });
        };

        var hookTask = _hook.RunAsync();
        Console.WriteLine("Avalonia run loop is active; hook started on its own thread.");

        _ = Task.Run(async () =>
        {
            while (!_hook.IsRunning) await Task.Delay(20);
            Console.WriteLine($"hook running alongside Avalonia: {_hook.IsRunning}");

            using var sim = EventSimulator.Create("Pisum Spike", SharpHook.Providers.UioHookProvider.Instance);
            for (var i = 0; i < 3; i++)
            {
                await Task.Delay(700);
                foreach (var k in new[] { KeyCode.VcLeftControl, KeyCode.VcLeftShift, KeyCode.VcSpace })
                    sim.SimulateKeyPress(k);
                await Task.Delay(400);
                foreach (var k in new[] { KeyCode.VcSpace, KeyCode.VcLeftShift, KeyCode.VcLeftControl })
                    sim.SimulateKeyRelease(k);
            }

            await Task.Delay(800);
            _hook.Stop();
            await hookTask;

            var pass = _presses > 0 && _releases > 0 && _uiThreadUpdates >= 6;
            Console.WriteLine($"\npresses={_presses} releases={_releases} ui-thread icon updates={_uiThreadUpdates}");
            Console.WriteLine($"CO-EXISTENCE VERDICT (Windows): {(pass ? "PASS - hook and Avalonia run loop coexist; hook->UI marshalling works" : "FAIL")}");

            Dispatcher.UIThread.Post(() => { _tray?.Dispose(); desktop.Shutdown(pass ? 0 : 1); });
        });

        base.OnFrameworkInitializationCompleted();
    }
}
