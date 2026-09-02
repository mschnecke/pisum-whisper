namespace Pisum.Whisper.App;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.App.Notifications;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Notifications;
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
    /// <summary>The reference's own wording (<c>lib.rs:588</c>), for this application's name.</summary>
    private const string WelcomeTitle = "Welcome to Pisum Whisper!";

    private const string WelcomeMessage = "Please configure an AI provider to get started.";

    private const string LoggingUnavailableTitle = "Logging Unavailable";

    private const string HotkeyUnavailableTitle = "Hotkey Unavailable";

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

    // Created on first open and kept, so a partly typed entry and the selected tab survive a hide.
    // Not created at startup: constructing six views and their view models would sit between launch
    // and the tray icon appearing, for a window most sessions never open.
    private SettingsWindow? _settingsWindow;

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

        // The settings window needs a theme and the tray icon never did, which is why
        // Avalonia.Themes.Fluent has been pinned and unreferenced since change 1. The window pins
        // ThemeVariant.Light on itself; FluentTheme would otherwise follow the OS and turn the
        // no-dark-theme non-goal into an untested dark theme.
        Styles.Add(new FluentTheme());
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

            var notifications = _services.GetRequiredService<INotificationService>();

            ShowFirstLaunch(_settings, notifications, ShowSettings);

            // Beside the welcome, and for the same reason it is here rather than where it is known:
            // both conditions are discovered in Program, before any dispatcher pumps a queue.
            ReportStartupConditions(
                _services.GetRequiredService<LogDirectory>(),
                _services.GetRequiredService<IGlobalHotkeyService>(),
                notifications);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The first-launch flow: say the application is running and needs configuring, then put the
    /// window it is pointing at on screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It lives here rather than in <c>Program.LoadSettings</c>, where
    /// <see cref="SettingsStore.IsFirstLaunch"/> is first known, because it shows a window and
    /// <c>Program</c> runs before Avalonia exists.
    /// </para>
    /// <para>
    /// <b>The order matters.</b> The welcome points at the window, so it is raised before the window
    /// is shown rather than landing on top of it.
    /// </para>
    /// <para>
    /// The reference does a third thing here — enabling autostart (<c>lib.rs:583</c>) — and this
    /// deliberately does not. <c>AutostartReconciler</c> has already reconciled the setting in its
    /// <c>StartAsync</c>, which covers the first launch and every later one through one path.
    /// </para>
    /// <para>
    /// Static and taking its three collaborators, so the flow is assertable without constructing an
    /// <see cref="App"/> — whose constructor opens tray assets and whose initialisation registers a
    /// native tray icon, neither of which a headless platform provides.
    /// </para>
    /// </remarks>
    internal static void ShowFirstLaunch(SettingsStore settings,
                                         INotificationService notifications,
                                         Action showSettings)
    {
        if (!settings.IsFirstLaunch)
        {
            return;
        }

        // Forced: a user who has never opened the settings window has never turned notifications
        // off either, but this is the one message the application cannot afford to have suppressed.
        notifications.Notify(WelcomeTitle, WelcomeMessage);
        showSettings();
    }

    /// <summary>
    /// Reports the two conditions that leave the application running but degraded: a log that is not
    /// being written, and a binding that is not being observed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It runs here rather than where the conditions are discovered.</b> Both are found in
    /// <c>Program</c> — one inside <c>AddFileLogging</c>, the other inside the hotkey service's
    /// start — and a notification is a window on a dispatcher that is not pumping until this method
    /// is reached. Reporting from there would enqueue a job into a loop that has not started.
    /// <see cref="ShowFirstLaunch"/> is the precedent, and it is the same argument.
    /// </para>
    /// <para>
    /// <b>Nothing is buffered, because there is nothing to buffer.</b> Both conditions are still true
    /// and still queryable by the time this runs: <c>host.Start()</c> has returned, so
    /// <see cref="IGlobalHotkeyService.Availability"/> is settled, and <see cref="LogDirectory"/> is
    /// a registered singleton holding the reason it discarded before. A replay queue would store what
    /// can simply be asked for.
    /// </para>
    /// <para>
    /// <b>Subscribe first, read second.</b> Reading first would lose a transition that landed between
    /// the two, which is the same reasoning as the tray icon's seeded <c>ApplyState</c>. The last
    /// reported value is what stops the seed and the event both reporting the same state — and it is
    /// shared under a lock because the event arrives on whichever thread the hook's run task faulted
    /// on, while the seed is read on this one.
    /// </para>
    /// <para>
    /// <b>Both are forced.</b> The preference exists to silence chatter; a hotkey that does not work
    /// makes the application inert, and a log that is not being written removes the only place the
    /// user could have found that out.
    /// </para>
    /// <para>
    /// Static and taking its collaborators, for the same reason <see cref="ShowFirstLaunch"/> is.
    /// </para>
    /// </remarks>
    internal static void ReportStartupConditions(LogDirectory logs,
                                                 IGlobalHotkeyService hotkeys,
                                                 INotificationService notifications)
    {
        if (logs.FailureReason is { } reason)
        {
            notifications.Notify(
                LoggingUnavailableTitle,
                $"Nothing is being written to the log. '{logs.Path}' could not be created: {reason}");
        }

        var gate = new Lock();
        HotkeyAvailability? lastReported = null;

        void ReportHotkey(HotkeyAvailability availability)
        {
            lock (gate)
            {
                if (lastReported == availability)
                {
                    return;
                }

                lastReported = availability;
            }

            if (availability == HotkeyAvailability.Available)
            {
                return;
            }

            notifications.Notify(HotkeyUnavailableTitle, HotkeyMessageFor(availability));
        }

        hotkeys.AvailabilityChanged += (_, availability) => ReportHotkey(availability);
        ReportHotkey(hotkeys.Availability);
    }

    /// <summary>
    /// What the user is told when the binding is not being observed.
    /// </summary>
    /// <remarks>
    /// The two permission states are kept apart because their remedies differ, which is the same
    /// reason <see cref="HotkeyAvailability"/> distinguishes them. The wording follows
    /// <c>GlobalHotkeyService</c>'s own log lines, shortened to what fits a notification; the detail
    /// is in the log, where the same failure has already been written at <c>Error</c>.
    /// </remarks>
    private static string HotkeyMessageFor(HotkeyAvailability availability)
    {
        return availability switch
        {
            HotkeyAvailability.PermissionNotGranted =>
                "Keys are not being observed, because permission to observe them has not been granted. "
                + "Grant it in System Settings > Privacy & Security > Accessibility, then restart the application.",

            HotkeyAvailability.PermissionRevoked =>
                "Keys are no longer being observed: access was withdrawn while the application was running. "
                + "Restore it in System Settings > Privacy & Security > Accessibility, then restart the application.",

            _ => "Keys are not being observed, so the hotkey will do nothing. Check the log for details.",
        };
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
        settingsItem.Click += (_, _) => ShowSettings();

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
            Menu = menu,
            IsVisible = true,
        };

        // Set after IsVisible rather than in the initializer: on macOS the native NSStatusItem is not
        // realised until IsVisible becomes true, and a ToolTipText assigned before that point is lost
        // rather than picked up by the native object once it exists — verified by hand under issue
        // #31's task 9/4.2, where the tooltip read a platform default ("Avalon Application") on first
        // launch and only became correct after a later settings change routed through ApplyTooltip.
        trayIcon.ToolTipText = TooltipFor(_settings!.Current);

        // Change 9 deferred this to change 10. Whether it fires on macOS when a Menu is attached is
        // open question 1: if it does not, the menu item is the entry point there and nothing else
        // changes.
        trayIcon.Clicked += (_, _) => ShowSettings();

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

    /// <summary>
    /// Brings the settings window up, creating it the first time it is asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Activate</c> is not optional: a hidden window shown while another application has focus can
    /// come up behind it.
    /// </para>
    /// <para>
    /// <c>IClassicDesktopStyleApplicationLifetime.MainWindow</c> is deliberately never assigned. It is
    /// null today and stays null. Assigning it is the natural thing to write here and is harmless
    /// <em>only</em> because <c>ShutdownMode.OnExplicitShutdown</c> is holding it up; it silently
    /// couples the application's lifetime to this window, and the coupling surfaces as "closing
    /// settings quits the application" the day someone changes the shutdown mode.
    /// </para>
    /// </remarks>
    private void ShowSettings()
    {
        _settingsWindow ??= new SettingsWindow(_services.GetRequiredService<SettingsWindowViewModel>());

        _settingsWindow.Show();
        _settingsWindow.Activate();
        _logger.LogDebug("Settings window shown.");
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

        // Whatever the user last typed, before the process goes. This and the flush on hide are the
        // two that bound the debounce's worst case to a killed process rather than an ordinary quit.
        if (_settingsWindow is not null)
        {
            _services.GetRequiredService<SettingsEditor>().FlushAsync().GetAwaiter().GetResult();
        }

        // A notification is a window on this dispatcher, and the dispatcher stops the moment this
        // returns. Closing them here is what keeps one from outliving the loop that owns it.
        _services.GetRequiredService<ToastPresenter>().CloseAll();

        // The tray icon owns a native handle. Releasing it here is what lets an immediate relaunch
        // succeed rather than find the previous icon still registered.
        _trayIcon?.Dispose();
        _trayIcon = null;
        _logger.LogDebug("Tray icon released; exiting with code {ExitCode}.", e.ApplicationExitCode);
    }
}
