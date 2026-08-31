namespace Pisum.Whisper.Core.Tests.Hotkeys;

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Tests.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SharpHook.Data;
using SharpHook.Testing;

/// <summary>
/// Drives the real service over <see cref="TestProvider"/>, so the hook wiring, the channel and the
/// dispatch loop are all exercised — only the operating system is replaced.
/// </summary>
/// <remarks>
/// The binding is written into the settings file explicitly rather than relying on the defaults,
/// because those differ by platform and these assertions must not.
/// </remarks>
public abstract class GlobalHotkeyServiceTestBase : IDisposable
{
    protected const EventMask CtrlShift = EventMask.LeftCtrl | EventMask.LeftShift;

    private readonly RecordingSink _sink = new();

    private string _home = string.Empty;
    private SerilogLoggerFactory? _loggerFactory;
    private Serilog.Core.Logger? _serilog;

    protected TestProvider Provider { get; private set; } = null!;

    protected SettingsStore Settings { get; private set; } = null!;

    protected GlobalHotkeyService Service { get; private set; } = null!;

    protected RecordingLogSource LogSource { get; } = new();

    protected List<HotkeyEdge> Edges { get; } = [];

    protected string SettingsPath => Path.Combine(_home, ".pisum-whisper.json");

    protected GlobalHotkeyServiceTestBase()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);

        WriteSettings(new AppSettings { Hotkey = Binding("Space", "Ctrl", "Shift") });

        _serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
        _loggerFactory = new SerilogLoggerFactory(_serilog);

        Settings = new SettingsStore(NullLogger<SettingsStore>.Instance, SettingsPath);
        Settings.Load();

        Provider = new TestProvider(TestThreadingMode.Simple);
        Service = new GlobalHotkeyService(
            _loggerFactory.CreateLogger<GlobalHotkeyService>(),
            Settings,
            LogSource,
            Provider);

        Service.Pressed += (_, _) => Record(HotkeyEdge.Pressed);
        Service.Released += (_, _) => Record(HotkeyEdge.Released);
    }

    public void Dispose()
    {
        Service.Dispose();
        _loggerFactory?.Dispose();
        _serilog?.Dispose();
        Directory.Delete(_home, true);
    }

    protected static HotkeyBinding Binding(string key, params string[] modifiers)
    {
        return new HotkeyBinding { Modifiers = [.. modifiers], Key = key };
    }

    protected void WriteSettings(AppSettings settings)
    {
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, SettingsJsonContext.OnDisk.AppSettings));
    }

    protected Task StartAsync() => Service.StartAsync(CancellationToken.None);

    protected void Press(KeyCode key, EventMask mask = CtrlShift) => Post(EventType.KeyPressed, key, mask);

    protected void Release(KeyCode key, EventMask mask = CtrlShift) => Post(EventType.KeyReleased, key, mask);

    /// <summary>
    /// Posts an event and returns how long the posting thread was held. In simple threading mode the
    /// provider dispatches on the calling thread, so this is the hook thread's cost for the event.
    /// </summary>
    protected TimeSpan Post(EventType type, KeyCode key, EventMask mask)
    {
        var uioHookEvent = new UioHookEvent
        {
            Type = type,
            Mask = mask,
            Keyboard = new KeyboardEventData { KeyCode = key },
        };

        var stopwatch = Stopwatch.StartNew();
        Provider.PostEvent(ref uioHookEvent);
        return stopwatch.Elapsed;
    }

    /// <summary>Edges are raised on the dispatch thread, so assertions wait rather than race.</summary>
    protected bool WaitForEdges(int count)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            lock (Edges)
            {
                if (Edges.Count >= count)
                {
                    return true;
                }
            }

            Thread.Sleep(5);
        }

        lock (Edges)
        {
            return Edges.Count >= count;
        }
    }

    /// <summary>Waits for a log message containing <paramref name="fragment"/> to be written.</summary>
    protected bool WaitForLogMessageContaining(string fragment) => _sink.WaitForMessageContaining(fragment);

    /// <summary>
    /// Everything logged so far. Used for assertions that something was <b>not</b> logged, which
    /// must not pay a wait for a message that is never coming.
    /// </summary>
    protected IReadOnlyList<string> LogMessages => _sink.Messages;

    protected HotkeyEdge[] Observed()
    {
        lock (Edges)
        {
            return [.. Edges];
        }
    }

    private void Record(HotkeyEdge edge)
    {
        lock (Edges)
        {
            Edges.Add(edge);
        }
    }
}
