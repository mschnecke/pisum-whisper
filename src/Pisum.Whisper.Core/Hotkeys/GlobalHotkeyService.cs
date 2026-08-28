namespace Pisum.Whisper.Core.Hotkeys;

using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Settings;
using SharpHook;
using SharpHook.Data;
using SharpHook.Logging;
using SharpHook.Providers;

/// <summary>
/// Owns the one global keyboard hook this process is allowed to run, matches the configured binding
/// against it, and reports the binding's edges to the rest of the application.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing but matching happens on the hook thread.</b> Both platforms police it — Windows removes
/// a low-level hook that exceeds <c>LowLevelHooksTimeout</c> without raising anything, and macOS
/// disables an event tap that stops responding. The handlers here match, set
/// <see cref="HookEventArgs.SuppressEvent"/> and write to a channel; a separate dispatch loop raises
/// the events, so a consumer that takes a second to open a microphone cannot cost the user their
/// hotkey.
/// </para>
/// <para>
/// <b>This component sees every keystroke on the machine and records none of them.</b> The key code
/// of an event that is not the configured binding is never logged, at any level. What may be logged
/// is the binding itself, its edges, counts and outcomes.
/// </para>
/// </remarks>
public sealed class GlobalHotkeyService : IGlobalHotkeyService, IHostedService, IDisposable
{
    private readonly ILogger<GlobalHotkeyService> _logger;
    private readonly SettingsStore _settings;
    private readonly ILogSource _logSource;
    private readonly IGlobalHook _hook;
    private readonly HotkeyMatcher _matcher;
    private readonly Channel<HotkeyEdge> _edges;
    private readonly Lock _captureGate = new();

    private TaskCompletionSource<HotkeyCapture>? _capture;
    private Task? _dispatchTask;
    private Task? _hookTask;
    private bool _disposed;

    public GlobalHotkeyService(
        ILogger<GlobalHotkeyService> logger,
        SettingsStore settings,
        ILogSource logSource)
        : this(logger, settings, logSource, UioHookProvider.Instance)
    {
    }

    /// <summary>
    /// Constructs the service over an explicit hook provider, which is how the tests drive it
    /// without touching the machine — the same shape as <see cref="SettingsStore"/>'s explicit-path
    /// constructor.
    /// </summary>
    internal GlobalHotkeyService(
        ILogger<GlobalHotkeyService> logger,
        SettingsStore settings,
        ILogSource logSource,
        IGlobalHookProvider hookProvider)
    {
        _logger = logger;
        _settings = settings;
        _logSource = logSource;

        _matcher = new HotkeyMatcher(Compile(settings.Current.Hotkey));

        // Unbounded, single writer, single reader. Unbounded is safe because the producer is a human
        // holding a key; bounded with a drop policy is not, because a dropped Released leaves the
        // recording state machine believing the binding is still held.
        _edges = Channel.CreateUnbounded<HotkeyEdge>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        _hook = new SimpleGlobalHook(hookProvider);
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _hook.HookDisabled += OnHookDisabled;

        _logSource.MessageLogged += OnLibUioHookMessage;
        _settings.Changed += OnSettingsChanged;
    }

    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public HotkeyAvailability Availability { get; private set; } = HotkeyAvailability.NotStarted;

    public HotkeyChord Chord => _matcher.Chord;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _dispatchTask = Task.Run(DispatchAsync, CancellationToken.None);

        // Completed by the hook once it is actually observing; raced against the run task so a
        // failure to start is known before StartAsync returns rather than surfacing later.
        var enabled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnEnabled(object? sender, HookEventArgs e) => enabled.TrySetResult();
        _hook.HookEnabled += OnEnabled;

        try
        {
            _hookTask = RunHookAsync();

            // Whichever finishes first settles it: HookEnabled means the hook is observing, and the
            // run task finishing this early means it failed to start.
            var settled = await Task.WhenAny(enabled.Task, _hookTask).ConfigureAwait(false);

            if (settled == enabled.Task)
            {
                Availability = HotkeyAvailability.Available;
                _logger.LogInformation("Observing the hotkey {Binding}.", _matcher.Chord);
            }
        }
        finally
        {
            _hook.HookEnabled -= OnEnabled;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _hook.Stop();

        // The binding may be held at the moment the application is asked to stop, and the physical
        // release will never be seen. Owing a release and not paying it leaves every consumer
        // believing a dictation is still in progress.
        ReleaseIfEngaged();

        _edges.Writer.TryComplete();

        if (_dispatchTask is { } dispatch)
        {
            await dispatch.ConfigureAwait(false);
        }

        if (_hookTask is { } hook)
        {
            await hook.ConfigureAwait(false);
        }
    }

    public Task<HotkeyCapture> CaptureAsync(CancellationToken cancellationToken)
    {
        var capture = new TaskCompletionSource<HotkeyCapture>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_captureGate)
        {
            if (_capture is not null)
            {
                return Task.FromResult(HotkeyCapture.Cancelled);
            }

            _capture = capture;
        }

        // Entering capture stops the binding being matched, so a hold in progress is owed a release
        // exactly as it would be if observation had stopped.
        ReleaseIfEngaged();

        cancellationToken.Register(() =>
        {
            if (TryEndCapture(HotkeyCapture.Cancelled))
            {
                _logger.LogDebug("Hotkey capture cancelled.");
            }
        });

        _logger.LogDebug("Hotkey capture started; the configured binding is not matched until it ends.");
        return capture.Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _settings.Changed -= OnSettingsChanged;
        _logSource.MessageLogged -= OnLibUioHookMessage;
        _hook.KeyPressed -= OnKeyPressed;
        _hook.KeyReleased -= OnKeyReleased;
        _hook.HookDisabled -= OnHookDisabled;

        ReleaseIfEngaged();
        _edges.Writer.TryComplete();

        _hook.Dispose();
        _logSource.Dispose();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (TryCapturePress(e))
        {
            return;
        }

        Apply(e, _matcher.OnKeyPressed(e.Data.KeyCode, e.RawEvent.Mask, e.IsEventSimulated));
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (IsCapturing)
        {
            return;
        }

        Apply(e, _matcher.OnKeyReleased(e.Data.KeyCode, e.RawEvent.Mask, e.IsEventSimulated));
    }

    private void Apply(HookEventArgs e, MatchResult result)
    {
        if (result.Suppress)
        {
            e.SuppressEvent = true;
        }

        if (result.Edge is { } edge)
        {
            _edges.Writer.TryWrite(edge);
        }
    }

    private bool IsCapturing
    {
        get
        {
            lock (_captureGate)
            {
                return _capture is not null;
            }
        }
    }

    /// <summary>
    /// Handles a press while a capture is in progress, reporting whether it consumed the event. A
    /// modifier on its own is not a combination, so it is waited through rather than captured.
    /// </summary>
    private bool TryCapturePress(KeyboardHookEventArgs e)
    {
        if (!IsCapturing)
        {
            return false;
        }

        if (e.IsEventSimulated || ModifierGroups.FromKeyCode(e.Data.KeyCode) != HotkeyModifiers.None)
        {
            return true;
        }

        if (!KeyCodeMap.TryGetKeyName(e.Data.KeyCode, out var keyName))
        {
            TryEndCapture(HotkeyCapture.KeyNotSupported);
            return true;
        }

        var binding = new HotkeyBinding
        {
            Modifiers = [.. KeyCodeMap.GetModifierNames(ModifierGroups.FromEventMask(e.RawEvent.Mask))],
            Key = keyName,
        };

        TryEndCapture(new HotkeyCapture(HotkeyCaptureOutcome.Captured, binding));
        return true;
    }

    private bool TryEndCapture(HotkeyCapture result)
    {
        TaskCompletionSource<HotkeyCapture>? capture;

        lock (_captureGate)
        {
            capture = _capture;
            _capture = null;
        }

        return capture?.TrySetResult(result) == true;
    }

    private void OnHookDisabled(object? sender, HookEventArgs e) => ReleaseIfEngaged();

    private void ReleaseIfEngaged()
    {
        if (_matcher.Disengage())
        {
            _edges.Writer.TryWrite(HotkeyEdge.Released);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var chord = Compile(settings.Hotkey);

        // The hook keeps running: only the compiled binding is swapped. The reference unregisters
        // and re-registers because RegisterHotKey gave it no choice, which leaves a window with no
        // binding and a re-registration that can fail.
        if (_matcher.Rebind(chord))
        {
            _edges.Writer.TryWrite(HotkeyEdge.Released);
        }

        _logger.LogInformation("Hotkey rebound to {Binding}.", chord);
    }

    private HotkeyChord Compile(HotkeyBinding binding)
    {
        if (HotkeyChord.TryCompile(binding, out var chord, out var invalidToken))
        {
            return chord;
        }

        // Falling back rather than refusing to bind: a tray-only application with no hotkey and no
        // window gives the user nothing to go on. The settings file is left exactly as written —
        // SettingsStore owns every write to it — so the user's intent is not quietly overwritten.
        _logger.LogWarning(
            "The configured hotkey names '{InvalidToken}', which is not a known key or modifier; "
            + "falling back to {Binding} for this session. The settings file is unchanged.",
            invalidToken,
            HotkeyChord.Default);

        return HotkeyChord.Default;
    }

    private async Task RunHookAsync()
    {
        try
        {
            await _hook.RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true).ConfigureAwait(false);
        }
        catch (HookException exception)
        {
            RecordUnavailable(exception);
        }
    }

    private void RecordUnavailable(HookException exception)
    {
        switch (exception.Result)
        {
            case UioHookResult.ErrorAxApiDisabled:
                Availability = HotkeyAvailability.PermissionNotGranted;
                _logger.LogError(
                    exception,
                    "The hotkey cannot be observed because Accessibility access has not been granted. "
                    + "Grant it in System Settings > Privacy & Security > Accessibility, then restart "
                    + "the application.");
                break;

            case UioHookResult.ErrorAxApiRevoked:
                Availability = HotkeyAvailability.PermissionRevoked;
                _logger.LogError(
                    exception,
                    "The hotkey stopped being observed because Accessibility access was withdrawn "
                    + "while the application was running. Restore it in System Settings > Privacy & "
                    + "Security > Accessibility, then restart the application.");
                break;

            default:
                Availability = HotkeyAvailability.Failed;
                _logger.LogError(
                    exception,
                    "The hotkey cannot be observed: the global hook failed to start with {Result}.",
                    exception.Result);
                break;
        }
    }

    private async Task DispatchAsync()
    {
        await foreach (var edge in _edges.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            // Information rather than Debug: "the hotkey did nothing" is this product's primary
            // failure report, and at the default level a log with no edge in it cannot distinguish
            // a hook that never fired from a pipeline that dropped the result. Two lines per
            // dictation is nothing against the 1 MB size cap. No IsEnabled guard, per CLAUDE.md.
            // The binding and its edges are the only key information this component may write down.
            _logger.LogInformation("Hotkey {Binding} {Edge}.", _matcher.Chord, edge);

            try
            {
                if (edge == HotkeyEdge.Pressed)
                {
                    Pressed?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Released?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception exception)
            {
                // A consumer that throws must not take the dispatch loop with it: the next edge is
                // very likely the Released that ends a recording.
                _logger.LogError(exception, "A {Edge} handler threw.", edge);
            }
        }
    }

    private void OnLibUioHookMessage(object? sender, LogEventArgs e)
    {
        // Warning and above only, filtered here rather than trusting the source's own minimum level.
        // libuiohook logs per event at debug, so a chattier source handed to this constructor would
        // otherwise turn the log file into a keylog — that is a guarantee worth making structural.
        if (e.LogEntry.Level is SharpHook.Data.LogLevel.Debug or SharpHook.Data.LogLevel.Info)
        {
            return;
        }

        var level = e.LogEntry.Level == SharpHook.Data.LogLevel.Error
            ? Microsoft.Extensions.Logging.LogLevel.Error
            : Microsoft.Extensions.Logging.LogLevel.Warning;

        _logger.Log(level, "libuiohook: {Message}", e.LogEntry.FullText);
    }
}
