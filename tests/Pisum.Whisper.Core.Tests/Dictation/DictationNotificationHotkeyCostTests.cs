namespace Pisum.Whisper.Core.Tests.Dictation;

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Notifications;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Tests.Hotkeys;
using Pisum.Whisper.Core.Transcription;
using SharpHook.Data;
using SharpHook.Testing;
using Shouldly;

/// <summary>
/// Task 3.2 — that a notification cannot cost the user their hotkey, measured rather than argued.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>GlobalHotkeyServiceTests.SlowConsumer_DoesNotHoldTheHookThread</c> one layer out. There
/// the slow consumer was an arbitrary handler; here it is the notification transport, reached through
/// the real <see cref="GlobalHotkeyService"/>, the real <see cref="DictationOrchestrator"/> and the
/// real <c>NotificationService</c> — only the presenter and the operating system are replaced.
/// </para>
/// <para>
/// <b>What it proves:</b> the notification is raised on the dispatch loop and never on the hook
/// thread, so a transport that takes a second — the <c>Process.Start</c> per notification this change
/// rejected being the concrete example — cannot exceed <c>LowLevelHooksTimeout</c> and have the hook
/// silently removed, and the release edge that ends a hold-to-record dictation is still delivered
/// rather than lost. That the transport in fact returns immediately is <c>ToastPresenter</c>'s
/// contract, asserted by
/// <c>App.Tests.Notifications.ToastPresenterTests.PresentReturnsBeforeTheWindowExists</c>.
/// </para>
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class DictationNotificationHotkeyCostTests : IDisposable
{
    private const EventMask CtrlShift = EventMask.LeftCtrl | EventMask.LeftShift;

    /// <summary>Long enough that holding the hook thread for it would have Windows remove the hook.</summary>
    private static readonly TimeSpan PresentCost = TimeSpan.FromSeconds(1);

    private readonly string _home;

    private readonly TestProvider _provider = new();

    private readonly RecordingLogSource _logSource = new();

    private readonly BlockingNotificationPresenter _presenter;

    private readonly GlobalHotkeyService _hotkeys;

    private readonly DictationOrchestrator _orchestrator;

    private readonly List<HotkeyEdge> _edges = [];

    public DictationNotificationHotkeyCostTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);

        var path = Path.Combine(_home, ".pisum-whisper.json");

        // Written explicitly rather than defaulted: the default binding differs by platform and this
        // assertion must not.
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new AppSettings {Hotkey = new HotkeyBinding {Modifiers = ["Ctrl", "Shift"], Key = "Space"}},
                SettingsJsonContext.OnDisk.AppSettings));

        var settings = new SettingsStore(NullLogger<SettingsStore>.Instance, path);
        settings.Load();

        _hotkeys = new GlobalHotkeyService(
            NullLogger<GlobalHotkeyService>.Instance,
            settings,
            _logSource,
            _provider);

        _hotkeys.Pressed += (_, _) => Record(HotkeyEdge.Pressed);
        _hotkeys.Released += (_, _) => Record(HotkeyEdge.Released);

        _presenter = new BlockingNotificationPresenter(PresentCost);

        _orchestrator = new DictationOrchestrator(
            NullLogger<DictationOrchestrator>.Instance,
            _hotkeys,
            settings,
            new FakeAudioCapture(),
            new FakeAudioEncoder(),
            new HangingProvider(),
            new FakeTextOutput(),
            new NotificationService(settings, _presenter),

            // No minimum duration: the two edges below are posted back to back on one thread, and
            // the real 50 ms would discard that as a brush before it ever reached Transcribing.
            minimumDuration: TimeSpan.Zero);
    }

    public void Dispose()
    {
        _orchestrator.Dispose();
        _hotkeys.Dispose();
        Directory.Delete(_home, true);
    }

    [Fact]
    public async Task ASlowNotificationNeverHoldsTheHookThread()
    {
        await _hotkeys.StartAsync(CancellationToken.None);

        // The first press and release are an ordinary dictation, which leaves the pipeline hanging
        // in the transcription — the state a further press has to be told about.
        Post(EventType.KeyPressed, KeyCode.VcSpace);
        Post(EventType.KeyReleased, KeyCode.VcSpace);
        WaitForEdges(2).ShouldBeTrue("the first dictation's two edges should have arrived");

        // The press that raises "Transcription In Progress", and with it a presenter that takes a
        // second. This is the edge on whose dispatch the notification runs.
        var pressCost = Post(EventType.KeyPressed, KeyCode.VcSpace);
        _presenter.Entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ShouldBeTrue("the notification should have been raised");

        // Posted while the presenter is still inside its second. On the hook thread this is the
        // release that ends a hold-to-record dictation, and it must cost the machine nothing.
        var releaseCost = Post(EventType.KeyReleased, KeyCode.VcSpace);

        pressCost.ShouldBeLessThan(TimeSpan.FromMilliseconds(500));
        releaseCost.ShouldBeLessThan(TimeSpan.FromMilliseconds(500));

        WaitForEdges(4).ShouldBeTrue("the release must still be reported, however slow the notification is");
        Observed().ShouldBe(
            [HotkeyEdge.Pressed, HotkeyEdge.Released, HotkeyEdge.Pressed, HotkeyEdge.Released],
            "edges must stay in order");

        _presenter.Count.ShouldBe(1);

        await _orchestrator.StopAsync(CancellationToken.None);
        await _hotkeys.StopAsync(CancellationToken.None);
    }

    /// <summary>Posts an event and returns what the posting thread — the hook thread — was held for.</summary>
    private TimeSpan Post(EventType type, KeyCode key)
    {
        var uioHookEvent = new UioHookEvent
        {
            Type = type,
            Mask = CtrlShift,
            Keyboard = new KeyboardEventData {KeyCode = key},
        };

        var stopwatch = Stopwatch.StartNew();
        _provider.PostEvent(ref uioHookEvent);
        return stopwatch.Elapsed;
    }

    private bool WaitForEdges(int count)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            lock (_edges)
            {
                if (_edges.Count >= count)
                {
                    return true;
                }
            }

            Thread.Sleep(5);
        }

        lock (_edges)
        {
            return _edges.Count >= count;
        }
    }

    private HotkeyEdge[] Observed()
    {
        lock (_edges)
        {
            return [.. _edges];
        }
    }

    private void Record(HotkeyEdge edge)
    {
        lock (_edges)
        {
            _edges.Add(edge);
        }
    }

    /// <summary>
    /// The transport this change rejected, in the only form a test can hold: one that does its work
    /// on the caller's thread instead of posting it.
    /// </summary>
    private sealed class BlockingNotificationPresenter(TimeSpan cost) : INotificationPresenter
    {
        private int _count;

        public ManualResetEventSlim Entered { get; } = new();

        public int Count => Volatile.Read(ref _count);

        public void Present(string title, string message)
        {
            Interlocked.Increment(ref _count);
            Entered.Set();
            Thread.Sleep(cost);
        }
    }

    /// <summary>Never returns on its own, so the pipeline stays in <c>Transcribing</c> for the second press.</summary>
    private sealed class HangingProvider : ITranscriptionProvider
    {
        public async Task<string> TranscribeAsync(EncodedAudio audio,
                                                  string systemPrompt,
                                                  CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return string.Empty;
        }
    }
}
