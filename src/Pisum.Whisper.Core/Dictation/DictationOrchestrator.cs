namespace Pisum.Whisper.Core.Dictation;

using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;

/// <summary>
/// The recording state machine: it turns the hotkey's two edges into a recording, and a recording
/// into text at the cursor. Every timing rule and every concurrency guard in this application lives
/// here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing but a state transition runs on the hotkey's dispatch thread.</b>
/// <see cref="GlobalHotkeyService"/> raises <see cref="IGlobalHotkeyService.Pressed"/> synchronously
/// from its channel read loop, so a handler that awaits the pipeline blocks that loop — and in
/// hold-to-record the very next thing in that channel is the release that ends the recording. The
/// handlers below claim a transition and return; everything with a duration runs on a pooled task.
/// </para>
/// <para>
/// <b>The transcript is never logged</b>, at any level, per the rules in <c>CLAUDE.md</c>. Character
/// counts, categories, outcomes and elapsed times are. Hotkey edges are not logged here either:
/// <see cref="GlobalHotkeyService"/> already writes one line per edge at <c>Information</c>.
/// </para>
/// </remarks>
public sealed class DictationOrchestrator : IHostedService, IDisposable
{
    /// <summary>
    /// The reference's <c>MIN_RECORDING_DURATION</c>. Below this the press was a brush and is
    /// discarded in silence — an accident should do nothing at all, which includes not raising an
    /// error.
    /// </summary>
    private static readonly TimeSpan DefaultMinimumDuration = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The reference's <c>TOGGLE_DEBOUNCE</c>, kept for a reason the reference does not give. Its
    /// stated purpose is keyboard auto-repeat, which <see cref="HotkeyMatcher"/> already absorbs
    /// without raising an edge — a held binding produces exactly one press. What it still covers is
    /// a fumbled double-tap in toggle mode: between 50 ms and 200 ms a recording escapes the
    /// minimum-duration discard, so without this it would be encoded, uploaded, and earn the user a
    /// transcription error for a slip of the finger.
    /// </summary>
    private static readonly TimeSpan DefaultDebounceWindow = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long a transcription may take in total, across every retry and every configured provider.
    /// </summary>
    /// <remarks>
    /// The 60 s timeout on the Gemini client is per <em>request</em>, and three attempts across N
    /// keys multiply it: a hung connection costs 183 s per key, so six minutes with two keys
    /// configured, throughout which the hotkey does nothing and says nothing. 120 s allows one full
    /// request and one retry, which covers a legitimate transcription — a fast failure such as a
    /// rejected key never approaches it, because it is not retried at all.
    /// </remarks>
    private static readonly TimeSpan DefaultTranscriptionBudget = TimeSpan.FromSeconds(120);

    private const string InProgressTitle = "Transcription In Progress";

    private const string InProgressMessage = "Please wait for the current transcription to finish.";

    private const string AutoStoppedTitle = "Recording Auto-Stopped";

    private const string PasteFailedTitle = "Paste Failed";

    private const string PasteFailedMessage =
        "The text was copied to the clipboard but could not be pasted. Paste it manually.";

    private readonly ILogger<DictationOrchestrator> _logger;

    private readonly IGlobalHotkeyService _hotkeys;

    private readonly SettingsStore _settings;

    private readonly IAudioCapture _capture;

    private readonly IAudioEncoder _encoder;

    private readonly ITranscriptionProvider _provider;

    private readonly ITextOutput _output;

    private readonly TimeSpan _minimumDuration;

    private readonly TimeSpan _debounceWindow;

    private readonly TimeSpan _transcriptionBudget;

    private readonly Func<long> _timestamp;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>
    /// Guards every mutable field below. Uncontended in practice — hotkey edges arrive one at a time
    /// on a single dispatch thread, and the only other takers are the watchdog and the pipeline's
    /// completion, each of which runs once per dictation.
    /// </summary>
    private readonly Lock _gate = new();

    private readonly CancellationTokenSource _shutdown = new();

    private DictationState _state = DictationState.Idle;

    /// <summary>
    /// Whether the capture device is open <em>or still closing</em>, which is not the same as
    /// <see cref="_state"/> being <see cref="DictationState.Recording"/>. A discarded short press
    /// announces <see cref="DictationState.Idle"/> the moment it is claimed, while
    /// <see cref="IAudioCapture.StopAsync"/> is still tearing the device down, and opening a second
    /// capture in that window throws. Nothing may open the device while this is set.
    /// </summary>
    private bool _capturing;

    private long _recordingStartedAt;

    private long _lastTogglePressAt;

    private bool _hasSeenTogglePress;

    private CancellationTokenSource? _watchdog;

    private Task _pipeline = Task.CompletedTask;

    private bool _disposed;

    public DictationOrchestrator(
        ILogger<DictationOrchestrator> logger,
        IGlobalHotkeyService hotkeys,
        SettingsStore settings,
        IAudioCapture capture,
        IAudioEncoder encoder,
        ITranscriptionProvider provider,
        ITextOutput output)
        : this(logger, hotkeys, settings, capture, encoder, provider, output, minimumDuration: null)
    {
    }

    /// <summary>
    /// Constructs the orchestrator over explicit timings and an explicit clock, which is how the
    /// tests drive every rule here without waiting 50 ms, 200 ms or two minutes — the same shape
    /// <see cref="GlobalHotkeyService"/> and <see cref="TextOutput"/> already use.
    /// </summary>
    /// <remarks>
    /// The delay is injected rather than only the maximum duration, because the maximum comes from
    /// settings in whole seconds and the watchdog could not otherwise be exercised in less than a
    /// second of real time — the same reason <see cref="GeminiProvider"/> injects its backoff.
    /// </remarks>
    internal DictationOrchestrator(
        ILogger<DictationOrchestrator> logger,
        IGlobalHotkeyService hotkeys,
        SettingsStore settings,
        IAudioCapture capture,
        IAudioEncoder encoder,
        ITranscriptionProvider provider,
        ITextOutput output,
        TimeSpan? minimumDuration = null,
        TimeSpan? debounceWindow = null,
        TimeSpan? transcriptionBudget = null,
        Func<long>? timestamp = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _logger = logger;
        _hotkeys = hotkeys;
        _settings = settings;
        _capture = capture;
        _encoder = encoder;
        _provider = provider;
        _output = output;
        _minimumDuration = minimumDuration ?? DefaultMinimumDuration;
        _debounceWindow = debounceWindow ?? DefaultDebounceWindow;
        _transcriptionBudget = transcriptionBudget ?? DefaultTranscriptionBudget;
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _delay = delay ?? Task.Delay;

        // Subscribed here rather than in StartAsync: the host resolves every IHostedService before
        // it starts any of them, so this runs before GlobalHotkeyService.StartAsync and no edge can
        // be missed in the window where the hook is coming up.
        _hotkeys.Pressed += OnPressed;
        _hotkeys.Released += OnReleased;
    }

    /// <summary>Raised whenever <see cref="State"/> changes, on a pooled thread.</summary>
    public event EventHandler<DictationState>? StateChanged;

    /// <summary>What the application is currently doing about a dictation.</summary>
    public DictationState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Stops observing the hotkey, discards a recording in progress, and <b>waits</b> for a dictation
    /// already past the microphone.
    /// </summary>
    /// <remarks>
    /// The waiting is a correctness requirement, not tidiness. Between <see cref="ITextOutput"/>
    /// writing the transcript to the clipboard and restoring what was there before, the user's
    /// previous clipboard contents exist nowhere but inside that call — and on Windows
    /// <c>SetClipboardData</c> hands ownership to the system, so the transcript outlives this
    /// process. Cancelling without awaiting lets the process exit inside that window and destroys the
    /// user's clipboard permanently.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _hotkeys.Pressed -= OnPressed;
        _hotkeys.Released -= OnReleased;

        CancellationTokenSource? watchdog;
        Task pipeline;
        bool recording;

        lock (_gate)
        {
            watchdog = TakeWatchdog();
            pipeline = _pipeline;
            recording = _state == DictationState.Recording;
        }

        CancelAndDispose(watchdog);

        // Shortens the delivery's wait before its restore, and abandons a transcription outright.
        await _shutdown.CancelAsync().ConfigureAwait(false);

        if (recording)
        {
            // Nobody is waiting on a dictation they did not finish, so the samples go.
            _logger.LogInformation("Shutting down while recording; the recording is discarded.");

            try
            {
                await StopCaptureAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "The capture could not be stopped cleanly during shutdown.");
            }

            SetStateAndAnnounce(DictationState.Idle);
        }

        await pipeline.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _hotkeys.Pressed -= OnPressed;
        _hotkeys.Released -= OnReleased;

        CancellationTokenSource? watchdog;

        lock (_gate)
        {
            watchdog = TakeWatchdog();
        }

        CancelAndDispose(watchdog);
        _shutdown.Dispose();
    }

    private void OnPressed(object? sender, EventArgs e)
    {
        if (_settings.Current.RecordingMode == RecordingMode.Toggle)
        {
            OnTogglePressed();
            return;
        }

        TryStartRecording();
    }

    private void OnReleased(object? sender, EventArgs e)
    {
        // In toggle mode the release means nothing at all; the reference ignores it in the same place.
        if (_settings.Current.RecordingMode == RecordingMode.Toggle)
        {
            return;
        }

        TryStopRecording();
    }

    private void OnTogglePressed()
    {
        bool recording;

        lock (_gate)
        {
            var now = _timestamp();

            // The timestamp is recorded before the state is read, so the window debounces a press in
            // either direction — the reference does the same (manager.rs:152-161).
            if (_hasSeenTogglePress
                && Stopwatch.GetElapsedTime(_lastTogglePressAt, now) < _debounceWindow)
            {
                _logger.LogDebug("A toggle press arrived inside the debounce window and was ignored.");
                return;
            }

            _lastTogglePressAt = now;
            _hasSeenTogglePress = true;
            recording = _state == DictationState.Recording;
        }

        if (recording)
        {
            TryStopRecording();
            return;
        }

        TryStartRecording();
    }

    private void TryStartRecording()
    {
        lock (_gate)
        {
            if (_state == DictationState.Transcribing)
            {
                // The second guard, and the only one that says anything: the user pressed the key
                // and is owed an explanation for nothing happening. Change 11 makes this a
                // notification; today it is a log line.
                _logger.LogInformation(
                    "The hotkey was pressed while a transcription is in progress; no recording was "
                    + "started. {Title}: {Message}",
                    InProgressTitle,
                    InProgressMessage);

                return;
            }

            // The first guard: already recording, or a discarded recording whose device has not
            // finished closing. Silent by design — the reference returns without a word.
            if (_state != DictationState.Idle || _capturing)
            {
                return;
            }

            try
            {
                _capture.Start();
            }
            catch (Exception exception)
            {
                // The state does not move, so the next press tries again.
                var (title, message) = DictationFailure.Describe(exception);
                _logger.LogError(exception, "The recording could not be started. {Title}: {Message}", title, message);
                return;
            }

            _capturing = true;
            _recordingStartedAt = _timestamp();
            _state = DictationState.Recording;

            ArmWatchdog();
        }

        Announce(DictationState.Recording);
    }

    /// <summary>
    /// Claims the end of a recording and starts the pipeline. Three callers race for this — the
    /// release edge, a toggle press and the watchdog — and exactly one may win, or the same capture
    /// is stopped and transcribed twice.
    /// </summary>
    private void TryStopRecording()
    {
        TimeSpan elapsed;
        bool discard;
        CancellationTokenSource? watchdog;

        lock (_gate)
        {
            if (_state != DictationState.Recording)
            {
                return;
            }

            elapsed = Stopwatch.GetElapsedTime(_recordingStartedAt, _timestamp());
            discard = elapsed < _minimumDuration;
            watchdog = TakeWatchdog();

            // A discarded brush never announces Transcribing; it goes straight back to Idle, while
            // _capturing keeps the device from being reopened until it has closed.
            var claimed = discard ? DictationState.Idle : DictationState.Transcribing;
            _state = claimed;

            // The announcement is made by the pipeline task itself, as its first act, rather than by
            // this thread after the task is started. Announcing here would be a race the fakes win
            // routinely: a pipeline that finishes quickly announces Idle from its finally before
            // this thread reaches its own Announce, and a subscriber then sees Idle followed by
            // Transcribing — leaving change 9's icon stuck on "transcribing" after a fast dictation.
            // One thread announcing a dictation's whole lifetime makes the order correct by
            // construction. The state field itself still moves under this lock, at claim time.
            _pipeline = Task.Run(async () =>
            {
                Announce(claimed);
                await RunAsync(elapsed, discard).ConfigureAwait(false);
            });
        }

        CancelAndDispose(watchdog);
    }

    /// <summary>
    /// The whole pipeline, on a pooled thread. Everything is caught: an exception escaping here
    /// would become an unobserved task exception, which does not crash the process — it vanishes,
    /// leaves the state at <see cref="DictationState.Transcribing"/> for ever, and the hotkey
    /// answers "Transcription In Progress" until the application is restarted.
    /// </summary>
    private async Task RunAsync(TimeSpan elapsed, bool discard)
    {
        try
        {
            var samples = await StopCaptureAsync().ConfigureAwait(false);

            if (discard)
            {
                _logger.LogDebug(
                    "A {Elapsed:F0} ms press was below the {Minimum:F0} ms minimum, so the recording "
                    + "was discarded without a word.",
                    elapsed.TotalMilliseconds,
                    _minimumDuration.TotalMilliseconds);

                return;
            }

            await DictateAsync(samples, elapsed).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // The user asked to quit. Nothing is reported for something they did on purpose.
            _logger.LogDebug("The dictation was abandoned because the application is shutting down.");
        }
        catch (Exception exception)
        {
            var (title, message) = DictationFailure.Describe(exception);
            _logger.LogError(exception, "The dictation failed. {Title}: {Message}", title, message);
        }
        finally
        {
            SetStateAndAnnounce(DictationState.Idle);
        }
    }

    private async Task DictateAsync(float[] samples, TimeSpan elapsed)
    {
        if (samples.Length == 0)
        {
            // The wall clock has already ruled out a brush, which is what earns this the right to be
            // a fault rather than a silent discard: an input device that is muted, disconnected or
            // routed elsewhere produces exactly this, and the user cannot diagnose it from silence.
            throw new AudioException(
                "No audio was recorded. Check that a microphone is connected, selected as the "
                + "system default, and not muted.");
        }

        var settings = _settings.Current;

        _logger.LogInformation(
            "Transcribing a {Seconds:F1} s recording of {SampleCount} samples as {Format}.",
            elapsed.TotalSeconds,
            samples.Length,
            settings.AudioFormat);

        var audio = _encoder.Encode(samples, IAudioCapture.SampleRate, settings.AudioFormat);

        var transcript = await TranscribeAsync(audio, ActiveSystemPrompt(settings)).ConfigureAwait(false);

        // The delivery is deliberately outside the transcription budget: it spends more than a
        // second waiting before its restore by design, and an expired budget must not cut that
        // short. It gets the shutdown token alone.
        var outcome = await _output.DeliverAsync(transcript, _shutdown.Token).ConfigureAwait(false);

        if (outcome == TextOutputOutcome.ClipboardOnly)
        {
            // Not a failure: the user's speech survived and a manual paste still produces it.
            // TextOutput has already logged which guard stopped the paste, so this does not repeat
            // it — it records only what the user is to be told, for change 11 to say out loud.
            _logger.LogInformation(
                "The dictation was delivered to the clipboard only. {Title}: {Message}",
                PasteFailedTitle,
                PasteFailedMessage);
        }
    }

    private async Task<string> TranscribeAsync(EncodedAudio audio, string systemPrompt)
    {
        // Linked, so that quitting abandons the transcription as surely as the budget expiring does.
        // Which of the two fired is decided by the shutdown token, in RunAsync's filters.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        budget.CancelAfter(_transcriptionBudget);

        return await _provider
            .TranscribeAsync(audio, systemPrompt, budget.Token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The active preset's prompt. There is no fallback because <see cref="SettingsStore.Load"/>
    /// repairs an <see cref="AppSettings.ActivePresetId"/> that resolves to nothing back to the
    /// built-in default — the guarantee <see cref="ITranscriptionProvider"/> already cites as its
    /// reason for taking the prompt as a parameter.
    /// </summary>
    private static string ActiveSystemPrompt(AppSettings settings) =>
        settings.Presets.First(preset => preset.Id == settings.ActivePresetId).SystemPrompt;

    private async Task<float[]> StopCaptureAsync()
    {
        try
        {
            return await _capture.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            // The device is closed, so the next recording may open it — which for a discarded brush
            // is what lifts the guard, the state having already returned to Idle.
            lock (_gate)
            {
                _capturing = false;
            }
        }
    }

    /// <summary>
    /// Arms the maximum-duration watchdog. Called with <see cref="_gate"/> held.
    /// </summary>
    /// <remarks>
    /// A <see cref="CancellationTokenSource"/> and one delay, cancelled when the recording ends by
    /// any other route. The reference spawns a thread that sleeps the entire maximum on every single
    /// recording and leaks one per dictation (<c>manager.rs:229-247</c>).
    /// </remarks>
    private void ArmWatchdog()
    {
        var seconds = _settings.Current.MaxRecordingDurationSecs;
        var maximum = TimeSpan.FromSeconds(seconds);
        var watchdog = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);

        _watchdog = watchdog;

        _ = Task.Run(async () =>
        {
            try
            {
                await _delay(maximum, watchdog.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The recording ended on its own, which is the ordinary case.
                return;
            }

            _logger.LogInformation(
                "The recording reached the {Seconds} s maximum and was stopped automatically. "
                + "{Title}: {Message}",
                seconds,
                AutoStoppedTitle,
                $"Maximum recording duration ({seconds} sec) reached. Transcribing…");

            TryStopRecording();
        });
    }

    /// <summary>Takes the watchdog so it can be cancelled outside the lock. Called with <see cref="_gate"/> held.</summary>
    private CancellationTokenSource? TakeWatchdog()
    {
        var watchdog = _watchdog;
        _watchdog = null;
        return watchdog;
    }

    /// <summary>
    /// Cancelled outside <see cref="_gate"/>: a cancellation callback can run inline on the calling
    /// thread, and running arbitrary continuations under the state machine's lock invites a deadlock.
    /// </summary>
    private static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        source.Cancel();
        source.Dispose();
    }

    private void SetStateAndAnnounce(DictationState state)
    {
        lock (_gate)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
        }

        Announce(state);
    }

    /// <summary>
    /// Announces a state that has already been set. Always called outside <see cref="_gate"/>: a
    /// subscriber runs arbitrary code — change 9 marshals onto the UI thread — and holding the
    /// state machine's lock across that invites a deadlock.
    /// </summary>
    private void Announce(DictationState state)
    {
        _logger.LogDebug("The dictation state is now {State}.", state);
        StateChanged?.Invoke(this, state);
    }
}
