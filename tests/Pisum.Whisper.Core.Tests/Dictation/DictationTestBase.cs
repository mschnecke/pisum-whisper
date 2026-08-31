namespace Pisum.Whisper.Core.Tests.Dictation;

using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Tests.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

/// <summary>
/// Drives the real state machine over fakes for all five of its dependencies, an advanceable clock
/// and a delay the test fires by hand. Nothing here needs a microphone, a network, a keyboard or a
/// clipboard, and no test waits 50 ms, 200 ms or two minutes.
/// </summary>
public abstract class DictationTestBase : IDisposable
{
    protected const string Transcript = "the quick brown fox";

    private readonly RecordingSink _sink = new();

    private readonly List<DictationState> _states = [];

    private readonly Lock _statesGate = new();

    private readonly Logger? _serilog;

    private readonly SerilogLoggerFactory? _loggerFactory;

    private readonly string _home = string.Empty;

    private DictationOrchestrator? _orchestrator;

    protected FakeHotkeyService Hotkeys { get; } = new();

    protected FakeAudioCapture Capture { get; } = new();

    protected FakeAudioEncoder Encoder { get; } = new();

    protected FakeTranscriptionProvider Provider { get; } = new();

    protected FakeTextOutput Output { get; } = new();

    protected FakeClock Clock { get; } = new();

    protected FakeDelay Delay { get; } = new();

    protected SettingsStore Settings { get; private set; } = null!;

    /// <summary>Every state announced, in order, which is what change 9 will render.</summary>
    protected IReadOnlyList<DictationState> States
    {
        get
        {
            lock (_statesGate)
            {
                return [.. _states];
            }
        }
    }

    protected IReadOnlyList<string> LogMessages => _sink.Messages;

    /// <summary>Waits for a log line containing <paramref name="fragment"/>, so assertions do not race the pipeline.</summary>
    protected bool WaitForLog(string fragment)
    {
        return _sink.WaitForMessageContaining(fragment);
    }

    protected IReadOnlyList<LogEvent> LogEvents => _sink.Events;

    protected DictationTestBase()
    {
        _serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
        _loggerFactory = new SerilogLoggerFactory(_serilog);

        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);

        Settings = new SettingsStore(
            _loggerFactory.CreateLogger<SettingsStore>(),
            Path.Combine(_home, ".pisum-whisper.json"));

        Settings.Load();
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
        _loggerFactory?.Dispose();
        _serilog?.Dispose();
        Directory.Delete(_home, true);
    }

    /// <summary>
    /// The orchestrator under test. The minimum duration and debounce window default to values the
    /// fake clock can step either side of; the budget defaults to something no test reaches by
    /// accident.
    /// </summary>
    protected DictationOrchestrator Create(TimeSpan? minimumDuration = null,
                                           TimeSpan? debounceWindow = null,
                                           TimeSpan? transcriptionBudget = null)
    {
        var orchestrator = new DictationOrchestrator(
            _loggerFactory!.CreateLogger<DictationOrchestrator>(),
            Hotkeys,
            Settings,
            Capture,
            Encoder,
            Provider,
            Output,
            minimumDuration ?? TimeSpan.FromMilliseconds(50),
            debounceWindow ?? TimeSpan.FromMilliseconds(200),
            transcriptionBudget ?? TimeSpan.FromMinutes(5),
            Clock.Now,
            Delay.Wait);

        orchestrator.StateChanged += (_, state) =>
        {
            lock (_statesGate)
            {
                _states.Add(state);
            }
        };

        _orchestrator = orchestrator;
        return orchestrator;
    }

    /// <summary>Applies <paramref name="mutate"/> to the current settings and adopts them.</summary>
    protected void Configure(Action<AppSettings> mutate)
    {
        var settings = Settings.Current;
        mutate(settings);
        Settings.Save(settings);
    }

    /// <summary>
    /// Records for <paramref name="duration"/> of clock time and releases the hotkey, which is the
    /// shape of nearly every test here.
    /// </summary>
    protected void Dictate(TimeSpan duration)
    {
        Hotkeys.Press();
        Clock.Advance(duration);
        Hotkeys.Release();
    }

    /// <summary>Waits for <paramref name="condition"/>, so a test never races a pooled task.</summary>
    protected static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5).ConfigureAwait(false);
        }

        return condition();
    }

    /// <summary>
    /// Waits for the state machine to come back to rest. The pipeline runs on a pooled task, so a
    /// test that asserted immediately after releasing the hotkey would race it.
    /// </summary>
    protected static async Task SettleAsync(DictationOrchestrator orchestrator)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (orchestrator.State != DictationState.Idle && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5).ConfigureAwait(false);
        }

        // The state returns to Idle in the pipeline's finally, a moment before the task itself
        // completes; give the remaining continuations a turn so assertions on the fakes are stable.
        await Task.Delay(20).ConfigureAwait(false);
    }
}
