namespace Pisum.Whisper.App;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// Tray-only application shell. No window is ever shown; the process stays alive on the tray icon
/// alone, which is why the desktop lifetime runs with <see cref="ShutdownMode.OnExplicitShutdown"/>.
/// </summary>
/// <remarks>
/// The icon has one appearance per <see cref="DictationState"/>, all three of them, so it never has
/// to claim to be recording while the recording is already being uploaded. There is no theme
/// handling on either platform: macOS flags the icon as a template and lets AppKit tint it, and the
/// Windows art carries its own contrast.
/// </remarks>
public sealed class App : Application
{
    private readonly IServiceProvider _services;
    private readonly ILogger<App> _logger;

    // Loaded once, in the constructor: AssetLoader.Open reads a resource stream, and a state change
    // happens on every dictation edge, so re-reading three assets to swap between them is work that
    // never needed doing twice.
    private readonly WindowIcon _idleIcon;
    private readonly WindowIcon _recordingIcon;
    private readonly WindowIcon _transcribingIcon;

    private SettingsStore? _settings;
    private DictationOrchestrator? _dictation;
    private TrayIcon? _trayIcon;

    public App(IServiceProvider services)
    {
        _services = services;
        _logger = services.GetRequiredService<ILogger<App>>();

        // One platform check for the whole set. The macOS half is the black-and-alpha Template art,
        // flagged as a template in CreateTrayIcon so AppKit renders it from its alpha channel.
        var suffix = OperatingSystem.IsMacOS() ? "Template" : string.Empty;
        _idleIcon = LoadIcon($"tray-idle{suffix}");
        _recordingIcon = LoadIcon($"tray-recording{suffix}");
        _transcribingIcon = LoadIcon($"tray-transcribing{suffix}");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _logger.LogDebug("Service container built and resolved; initialising the tray icon.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Resolved here rather than in the constructor: this runs after host.Start(), so the
            // orchestrator singleton the host built is the one that comes back.
            _settings = _services.GetRequiredService<SettingsStore>();
            var dictation = _services.GetRequiredService<DictationOrchestrator>();
            _dictation = dictation;

            _trayIcon = CreateTrayIcon(desktop);
            desktop.Exit += OnExit;

            dictation.StateChanged += OnDictationStateChanged;
            _settings.Changed += OnSettingsChanged;

            // Seeded rather than assumed, because the orchestrator subscribes to the hotkey in its
            // constructor and is therefore armed from host.Start() — a hotkey pressed during
            // Avalonia's platform initialisation opens a recording this icon would otherwise
            // misreport as idle for the length of that dictation. State is read inside the callback
            // and not at post time: that puts the seed in the same queue at the same priority as
            // every real transition, so it can only ever lose to something newer.
            Dispatcher.UIThread.Post(() => ApplyState(dictation.State));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static WindowIcon LoadIcon(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://Pisum.Whisper.App/Assets/{name}.png"));
        return new WindowIcon(stream);
    }

    /// <summary>
    /// The tooltip text. The active preset's <b>name</b> and never its system prompt.
    /// </summary>
    /// <remarks>
    /// <see cref="SettingsStore.Load"/> repairs an <see cref="AppSettings.ActivePresetId"/> that
    /// resolves to nothing back to the built-in default, which is why this needs no fallback — the
    /// same guarantee <c>DictationOrchestrator.ActiveSystemPrompt</c> already relies on.
    /// </remarks>
    private static string TooltipFor(AppSettings settings)
    {
        var active = settings.Presets.First(preset => preset.Id == settings.ActivePresetId);
        return $"Pisum Whisper - {active.Name}";
    }

    private TrayIcon CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += (_, _) =>
        {
            _logger.LogDebug("Settings chosen; there is no settings window yet.");
        };

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) =>
        {
            _logger.LogDebug("Quit chosen; shutting down.");
            desktop.Shutdown();
        };

        var menu = new NativeMenu();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        var trayIcon = new TrayIcon
        {
            Icon = _idleIcon,
            ToolTipText = TooltipFor(_settings!.Current),
            Menu = menu,
            IsVisible = true,
        };

        if (OperatingSystem.IsMacOS())
        {
            // Where this application's macOS theme handling lives: AppKit renders a template image
            // from its alpha channel alone, tinting it for the menu bar's appearance and inverting
            // it under the click highlight. The accessors are on MacOSProperties and deliberately
            // not on TrayIcon, which carries no such member. Avalonia.Win32.TrayIconImpl does not
            // implement ITrayIconWithIsTemplateImpl, so the guard says why the line exists rather
            // than guarding against anything: on Windows the value is stored and never consumed.
            MacOSProperties.SetIsTemplateIcon(trayIcon, true);
        }

        return trayIcon;
    }

    private void OnDictationStateChanged(object? sender, DictationState state)
    {
        // Raised on a pooled thread. Post preserves order at equal priority, so a fast dictation's
        // Idle cannot overtake its own Transcribing.
        Dispatcher.UIThread.Post(() => ApplyState(state));
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        // Raised on whichever thread called Save, which will be the settings window's UI thread.
        Dispatcher.UIThread.Post(() => ApplyTooltip(settings));
    }

    private void ApplyState(DictationState state)
    {
        // Nothing else covers a pipeline task announcing between the Quit click and the dispatcher
        // loop stopping, by which point OnExit has already released the icon.
        if (_trayIcon is null)
        {
            return;
        }

        _logger.LogDebug("Dictation state is now {State}; updating the tray icon.", state);
        _trayIcon.Icon = IconFor(state);
    }

    private void ApplyTooltip(AppSettings settings)
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.ToolTipText = TooltipFor(settings);
        _logger.LogDebug("Active preset is now {PresetId}; tray tooltip updated.", settings.ActivePresetId);
    }

    /// <summary>
    /// The state's icon. An exhaustive switch expression on purpose: a fourth
    /// <see cref="DictationState"/> is then a compile error here — CS8509 under
    /// warnings-as-errors — rather than a silent fall-through to the idle appearance.
    /// </summary>
    private WindowIcon IconFor(DictationState state)
    {
        // CS8524 is the *unnamed* enum value — `(DictationState)3` — and it fires on every
        // arm-complete enum switch expression, so warnings-as-errors rejects one out of hand.
        // Suppressed rather than answered with a `_ =>` arm, which would silence CS8509 with it and
        // turn a fourth state from a build failure into a runtime one. Nothing casts an integer to
        // DictationState; the only values reaching here are the three named ones.
#pragma warning disable CS8524
        return state switch
        {
            DictationState.Idle => _idleIcon,
            DictationState.Recording => _recordingIcon,
            DictationState.Transcribing => _transcribingIcon,
        };
#pragma warning restore CS8524
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // Cheap insurance, not the invariant it resembles. Program.cs runs host.StopAsync in a
        // finally that executes after the Avalonia loop has returned, so DictationOrchestrator's
        // shutdown Idle is posted into a dispatcher nobody pumps and never arrives at all — which
        // is also why the clipboard restore's log lines appear after the release line below.
        if (_dictation is not null)
        {
            _dictation.StateChanged -= OnDictationStateChanged;
        }

        if (_settings is not null)
        {
            _settings.Changed -= OnSettingsChanged;
        }

        // The tray icon owns a native handle. Releasing it here is what lets an immediate relaunch
        // succeed rather than find the previous icon still registered.
        _trayIcon?.Dispose();
        _trayIcon = null;
        _logger.LogDebug("Tray icon released; exiting with code {ExitCode}.", e.ApplicationExitCode);
    }
}
