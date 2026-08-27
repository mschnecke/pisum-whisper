using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pisum.Whisper.App;

/// <summary>
/// Tray-only application shell. No window is ever shown; the process stays alive on the tray icon
/// alone, which is why the desktop lifetime runs with <see cref="ShutdownMode.OnExplicitShutdown"/>.
/// </summary>
public sealed class App : Application
{
    private static readonly Uri IdleIconUri = new("avares://Pisum.Whisper.App/Assets/tray-idle.png");

    private readonly ILogger<App> _logger;
    private TrayIcon? _trayIcon;

    public App(IServiceProvider services)
        => _logger = services.GetRequiredService<ILogger<App>>();

    public override void OnFrameworkInitializationCompleted()
    {
        _logger.LogDebug("Service container built and resolved; initialising the tray icon.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _trayIcon = CreateTrayIcon(desktop);
            desktop.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private TrayIcon CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) =>
        {
            _logger.LogDebug("Quit chosen; shutting down.");
            desktop.Shutdown();
        };

        var menu = new NativeMenu();
        menu.Items.Add(quit);

        using var iconStream = AssetLoader.Open(IdleIconUri);

        return new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Pisum Whisper",
            Menu = menu,
            IsVisible = true,
        };
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // The tray icon owns a native handle. Releasing it here is what lets an immediate relaunch
        // succeed rather than find the previous icon still registered.
        _trayIcon?.Dispose();
        _trayIcon = null;
        _logger.LogDebug("Tray icon released; exiting with code {ExitCode}.", e.ApplicationExitCode);
    }
}
