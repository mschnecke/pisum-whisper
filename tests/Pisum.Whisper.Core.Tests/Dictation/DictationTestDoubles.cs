namespace Pisum.Whisper.Core.Tests.Dictation;

using System.Diagnostics;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;

/// <summary>
/// Raises the two edges on demand, standing in for the hook. The orchestrator subscribes in its
/// constructor, so raising an edge here is exactly what a key press does in the running application.
/// </summary>
public sealed class FakeHotkeyService : IGlobalHotkeyService
{
    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public HotkeyAvailability Availability => HotkeyAvailability.Available;

    public HotkeyChord Chord => HotkeyChord.Default;

    /// <summary>Whether both handlers are still attached, which is how the shutdown tests see the unsubscribe.</summary>
    public bool HasSubscribers => Pressed is not null && Released is not null;

    public Task<HotkeyCapture> CaptureAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(HotkeyCapture.Cancelled);
    }

    public void Press()
    {
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    public void Release()
    {
        Released?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// An input device that records how it was driven and returns whatever samples the test supplies.
/// It reproduces <see cref="MiniAudioCapture"/>'s two invariants — starting twice throws, and
/// stopping without starting throws — because the state machine's whole job is to never do either.
/// </summary>
public sealed class FakeAudioCapture : IAudioCapture
{
    private readonly Lock _gate = new();

    private TaskCompletionSource? _stopGate;

    private bool _started;

    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public float[] Samples { get; set; } = [0.1f, 0.2f, 0.3f];

    /// <summary>Set to make <see cref="Start"/> throw, as a missing input device does.</summary>
    public Exception? StartFailure { get; set; }

    /// <summary>Set to make <see cref="StopAsync"/> throw.</summary>
    public Exception? StopFailure { get; set; }

    /// <summary>Holds <see cref="StopAsync"/> open until <see cref="ReleaseStop"/> is called.</summary>
    public void BlockStop()
    {
        lock (_gate)
        {
            _stopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void ReleaseStop()
    {
        TaskCompletionSource? gate;

        lock (_gate)
        {
            gate = _stopGate;
            _stopGate = null;
        }

        gate?.TrySetResult();
    }

    public void Start()
    {
        if (StartFailure is { } failure)
        {
            throw failure;
        }

        lock (_gate)
        {
            if (_started)
            {
                throw new InvalidOperationException("Capture is already started.");
            }

            _started = true;
            Starts++;
        }
    }

    public async Task<float[]> StopAsync()
    {
        Task? gate;

        lock (_gate)
        {
            if (!_started)
            {
                throw new InvalidOperationException("Capture was not started.");
            }

            gate = _stopGate?.Task;
        }

        if (gate is not null)
        {
            await gate.ConfigureAwait(false);
        }

        lock (_gate)
        {
            _started = false;
            Stops++;
        }

        if (StopFailure is { } failure)
        {
            throw failure;
        }

        return Samples;
    }
}

/// <summary>Records what it was asked to encode, so the rate and the format can be asserted.</summary>
public sealed class FakeAudioEncoder : IAudioEncoder
{
    public int Calls { get; private set; }

    public int SampleRate { get; private set; }

    public AudioFormat Preferred { get; private set; }

    public float[]? Samples { get; private set; }

    public EncodedAudio Result { get; set; } =
        new([1, 2, 3], EncodedAudio.OpusMimeType, AudioFormat.Opus);

    public Exception? Failure { get; set; }

    public EncodedAudio Encode(float[] samples, int sampleRate, AudioFormat preferred)
    {
        Calls++;
        Samples = samples;
        SampleRate = sampleRate;
        Preferred = preferred;

        if (Failure is { } failure)
        {
            throw failure;
        }

        return Result;
    }
}

/// <summary>
/// A provider that can answer, fail, or hang until the test lets it go — the last being how the
/// transcription budget and shutdown are exercised without waiting two minutes.
/// </summary>
public sealed class FakeTranscriptionProvider : ITranscriptionProvider
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Calls { get; private set; }

    public string? SystemPrompt { get; private set; }

    public EncodedAudio Audio { get; private set; }

    public string Result { get; set; } = "the quick brown fox";

    public Exception? Failure { get; set; }

    /// <summary>When set, the call never completes on its own and only the token ends it.</summary>
    public bool Hang { get; set; }

    /// <summary>Completes once the provider has been entered, so a test need not poll for it.</summary>
    public Task Entered => _entered.Task;

    public async Task<string> TranscribeAsync(EncodedAudio audio,
                                              string systemPrompt,
                                              CancellationToken cancellationToken)
    {
        Calls++;
        Audio = audio;
        SystemPrompt = systemPrompt;
        _entered.TrySetResult();

        if (Hang)
        {
            // Exactly what a hung upload looks like from here: nothing but the token ends it.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        return Result;
    }
}

/// <summary>Records the transcript it was handed and the token it was handed with.</summary>
public sealed class FakeTextOutput : ITextOutput
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TaskCompletionSource? _gate;

    public int Calls { get; private set; }

    public string? Transcript { get; private set; }

    public bool WasCancelled { get; private set; }

    public TextOutputOutcome Outcome { get; set; } = TextOutputOutcome.Pasted;

    public Exception? Failure { get; set; }

    public Task Entered => _entered.Task;

    /// <summary>Holds the delivery open, standing in for the second the real one spends before its restore.</summary>
    public void Block()
    {
        _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release()
    {
        _gate?.TrySetResult();
    }

    public async Task<TextOutputOutcome> DeliverAsync(string transcript, CancellationToken cancellationToken)
    {
        Calls++;
        Transcript = transcript;
        _entered.TrySetResult();

        if (_gate is { } gate)
        {
            await gate.Task.ConfigureAwait(false);
        }

        // The real delivery never abandons a restore it owes, so this records the cancellation
        // rather than throwing on it — the same contract TextOutput documents.
        WasCancelled = cancellationToken.IsCancellationRequested;

        if (Failure is { } failure)
        {
            throw failure;
        }

        return Outcome;
    }
}

/// <summary>
/// A monotonic clock the test advances by hand, so the 50 ms minimum and the 200 ms debounce are
/// exercised without any test sleeping. It produces <see cref="Stopwatch"/> ticks, because that is
/// what <see cref="Stopwatch.GetElapsedTime(long, long)"/> interprets.
/// </summary>
public sealed class FakeClock
{
    private long _ticks = Stopwatch.GetTimestamp();

    public long Now()
    {
        return Interlocked.Read(ref _ticks);
    }

    public void Advance(TimeSpan amount)
    {
        Interlocked.Add(ref _ticks, (long) (amount.TotalSeconds * Stopwatch.Frequency));
    }
}

/// <summary>
/// Stands in for <see cref="Task.Delay(TimeSpan, CancellationToken)"/> in the watchdog, so the
/// maximum recording duration can be reached instantly instead of after the configured seconds.
/// </summary>
public sealed class FakeDelay
{
    private readonly TaskCompletionSource _elapsed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TimeSpan? Requested { get; private set; }

    public Task Wait(TimeSpan duration, CancellationToken cancellationToken)
    {
        Requested = duration;
        return _elapsed.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Fires the watchdog.</summary>
    public void Elapse()
    {
        _elapsed.TrySetResult();
    }
}
