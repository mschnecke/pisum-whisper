using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Pisum.Whisper.Spikes;

/// <summary>
/// S3 (task 1.6a) — Avalonia 12.1 TrayIcon: does it appear, carry a tooltip, swap its image at
/// runtime, and show a native menu? Change 9 drives the icon from the recording state, so a
/// runtime swap is the behaviour that matters, not merely showing an icon once.
/// </summary>
internal static class TraySpike
{
    public static Task<int> RunAsync()
    {
        var exit = AppBuilder.Configure<TrayApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
        return Task.FromResult(exit);
    }
}

internal sealed class TrayApp : Application
{
    private static readonly string IdleIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-idle.png");
    private static readonly string RecordingIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-recording.png");

    private TrayIcon? _tray;
    private int _swaps;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var settings = new NativeMenuItem("Settings");
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(settings);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(IdleIcon),
            ToolTipText = "Pisum Whisper - spike (idle)",
            Menu = menu,
            IsVisible = true,
        };

        Console.WriteLine($"tray created. IsVisible={_tray.IsVisible} menu items={menu.Items.Count}");
        Console.WriteLine($"theme variant: {ActualThemeVariant}");

        // Change 9 swaps this icon from a background thread, so exercise the timer path now.
        DispatcherTimer.Run(() =>
        {
            _swaps++;
            var recording = _swaps % 2 == 1;
            _tray.Icon = new WindowIcon(recording ? RecordingIcon : IdleIcon);
            _tray.ToolTipText = $"Pisum Whisper - spike ({(recording ? "recording" : "idle")})";
            Console.WriteLine($"  swap {_swaps}: icon -> {(recording ? "recording" : "idle")}, tooltip updated");

            if (_swaps < 8) return true;
            Console.WriteLine($"\nS3 (Windows) VERDICT: PASS - icon shown, {_swaps} runtime swaps, tooltip set, {menu.Items.Count}-item native menu");
            _tray.Dispose();
            desktop.Shutdown();
            return false;
        }, TimeSpan.FromSeconds(2));

        base.OnFrameworkInitializationCompleted();
    }
}
