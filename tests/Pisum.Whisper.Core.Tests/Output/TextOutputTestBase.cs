namespace Pisum.Whisper.Core.Tests.Output;

using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Tests.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SharpHook.Data;
using SharpHook.Simulation;
using SharpHook.Testing;

/// <summary>
/// Drives the real sequence over a fake clipboard, a fake probe and <see cref="TestProvider"/>, so
/// nothing here needs a clipboard, a keyboard or a platform. The internal constructor supplies the
/// platform selection and both delays, which is what lets a Windows host assert the macOS keystroke
/// and what keeps a test that exercises the restore from waiting a second for it.
/// </summary>
public abstract class TextOutputTestBase
{
    protected const string Transcript = "the quick brown fox";

    private readonly RecordingSink _sink = new();

    private Serilog.Core.Logger? _serilog;

    private SerilogLoggerFactory? _loggerFactory;

    protected FakeClipboard Clipboard { get; } = new();

    protected FakePasteProbe Probe { get; } = new();

    protected TestProvider Provider { get; private set; } = null!;

    protected IEventSimulator Simulator { get; private set; } = null!;

    [TestInitialize]
    public void CreateSimulator()
    {
        _serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
        _loggerFactory = new SerilogLoggerFactory(_serilog);

        Provider = new TestProvider(TestThreadingMode.Simple);
        Simulator = EventSimulator.Create("Pisum Whisper Tests", Provider);
    }

    [TestCleanup]
    public void DisposeSimulator()
    {
        (Simulator as IDisposable)?.Dispose();
        _loggerFactory?.Dispose();
        _serilog?.Dispose();
    }

    /// <summary>
    /// A delivery whose delays are short enough to be waited through. The restore delay is still
    /// long enough to be observably shortened by a cancellation.
    /// </summary>
    protected TextOutput Create(bool macOs = false, TimeSpan? settleDelay = null, TimeSpan? restoreDelay = null)
    {
        return new TextOutput(
            _loggerFactory!.CreateLogger<TextOutput>(),
            Clipboard,
            Probe,
            Simulator,
            macOs,
            settleDelay ?? TimeSpan.Zero,
            restoreDelay ?? TimeSpan.FromMilliseconds(50));
    }

    /// <summary>The keystroke that reached the operating system, in order.</summary>
    protected (EventType Type, KeyCode Key)[] Posted =>
        [.. Provider.PostedEvents.Select(posted => (posted.Type, posted.Keyboard.KeyCode))];

    protected IReadOnlyList<string> LogMessages => _sink.Messages;

    protected IReadOnlyList<Serilog.Events.LogEvent> LogEvents => _sink.Events;
}
