namespace Pisum.Whisper.Core.Tests.Output;

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Tests.Hotkeys;
using SharpHook.Data;
using SharpHook.Simulation;
using SharpHook.Testing;
using Shouldly;

/// <summary>
/// Task 5.2 — one <see cref="TestProvider"/> backing both the hotkey service and the event
/// simulator, which is the only arrangement in which the application can be caught observing its own
/// paste.
/// </summary>
/// <remarks>
/// The hook is live throughout a dictation and sees injected events on the same path as physical
/// ones, so a paste the application reacted to would start another dictation from the one that just
/// finished. Change 6's <c>HotkeyMatcher</c> already returns <c>Ignore</c> for a simulated event on
/// both edges; this is the test that keeps that check from being deleted as dead code, which is why
/// the binding here is the paste combination itself.
/// </remarks>
[IntegrationTest]
public sealed class PasteIsNotObservedAsAHotkeyTests : IDisposable
{
    private readonly RecordingLogSource _logSource = new();

    private readonly List<HotkeyEdge> _edges = [];

    private string _home = string.Empty;

    private TestProvider _provider = null!;

    private GlobalHotkeyService _hotkeys = null!;

    private IEventSimulator _simulator = null!;

    public PasteIsNotObservedAsAHotkeyTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);

        var settingsPath = Path.Combine(_home, ".pisum-whisper.json");
        var settings = new AppSettings { Hotkey = new HotkeyBinding { Modifiers = ["Ctrl"], Key = "V" } };
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, SettingsJsonContext.OnDisk.AppSettings));

        var store = new SettingsStore(NullLogger<SettingsStore>.Instance, settingsPath);
        store.Load();

        _provider = new TestProvider(TestThreadingMode.Simple);
        _hotkeys = new GlobalHotkeyService(NullLogger<GlobalHotkeyService>.Instance, store, _logSource, _provider);
        _hotkeys.Pressed += (_, _) => Record(HotkeyEdge.Pressed);
        _hotkeys.Released += (_, _) => Record(HotkeyEdge.Released);

        _simulator = EventSimulator.Create("Pisum Whisper Tests", _provider);
    }

    public void Dispose()
    {
        (_simulator as IDisposable)?.Dispose();
        _hotkeys.Dispose();
        Directory.Delete(_home, true);
    }

    [Fact]
    public async Task ADeliveryWhileTheHookIsRunning_ReportsNoHotkeyEdges()
    {
        await _hotkeys.StartAsync(CancellationToken.None);

        var outcome = await CreateDelivery().DeliverAsync("a dictated sentence", CancellationToken.None);

        outcome.ShouldBe(TextOutputOutcome.Pasted);
        _provider.PostedEvents.Count.ShouldBe(4, "the keystroke did reach the operating system");

        await Task.Delay(150, TestContext.Current.CancellationToken);

        Observed().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheSameCombinationPressedByHand_DoesReportEdges()
    {
        // Without this the test above would pass just as well if the binding never matched anything,
        // and the check it exists to protect could be deleted with the suite still green.
        await _hotkeys.StartAsync(CancellationToken.None);

        Post(EventType.KeyPressed, KeyCode.VcV, EventMask.LeftCtrl);
        Post(EventType.KeyReleased, KeyCode.VcV, EventMask.LeftCtrl);

        WaitForEdges(2).ShouldBeTrue();
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);
    }

    private TextOutput CreateDelivery()
    {
        return new TextOutput(
            NullLogger<TextOutput>.Instance,
            new FakeClipboard(),
            new FakePasteProbe(),
            _simulator,
            macOs: false,
            TimeSpan.Zero,
            TimeSpan.Zero);
    }

    private void Post(EventType type, KeyCode key, EventMask mask)
    {
        var uioHookEvent = new UioHookEvent
        {
            Type = type,
            Mask = mask,
            Keyboard = new KeyboardEventData { KeyCode = key },
        };

        _provider.PostEvent(ref uioHookEvent);
    }

    private bool WaitForEdges(int count)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (Observed().Length >= count)
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return Observed().Length >= count;
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
}
