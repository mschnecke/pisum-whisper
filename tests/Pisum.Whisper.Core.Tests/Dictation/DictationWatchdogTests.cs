namespace Pisum.Whisper.Core.Tests.Dictation;

using Pisum.Whisper.Core.Dictation;
using Shouldly;

/// <summary>
/// Tasks 2.8 and 2.10 — the maximum-duration watchdog, and the three points at which the state is
/// announced.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class DictationWatchdogTests : DictationTestBase
{
    // ---- Task 2.8: the watchdog ----

    [Fact]
    public async Task TheWatchdogIsArmedWithTheConfiguredMaximum()
    {
        Configure(settings => settings.MaxRecordingDurationSecs = 42);
        var orchestrator = Create();

        Hotkeys.Press();

        (await WaitForAsync(() => Delay.Requested is not null)).ShouldBeTrue();
        Delay.Requested.ShouldBe(TimeSpan.FromSeconds(42));

        Clock.Advance(TimeSpan.FromSeconds(1));
        Hotkeys.Release();
        await SettleAsync(orchestrator);
    }

    /// <summary>
    /// The audio is real and the user was speaking, so reaching the maximum transcribes rather than
    /// discarding — and says why, which change 11 turns into a notification.
    /// </summary>
    [Fact]
    public async Task ReachingTheMaximumStopsTheRecordingAndTranscribesIt()
    {
        var orchestrator = Create();

        Hotkeys.Press();
        Clock.Advance(TimeSpan.FromMinutes(10));
        Delay.Elapse();
        await SettleAsync(orchestrator);

        Capture.Stops.ShouldBe(1);
        Provider.Calls.ShouldBe(1);
        Output.Calls.ShouldBe(1);
        WaitForLog("Recording Auto-Stopped").ShouldBeTrue();
    }

    /// <summary>
    /// A recording that ends normally leaves nothing running. The reference spawns a thread that
    /// sleeps the whole maximum on every recording and leaks one per dictation.
    /// </summary>
    [Fact]
    public async Task ANormalStopCancelsTheWatchdog()
    {
        var orchestrator = Create();

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        // The watchdog is already cancelled, so firing its delay must change nothing at all.
        Delay.Elapse();
        await SettleAsync(orchestrator);

        Capture.Stops.ShouldBe(1);
        Provider.Calls.ShouldBe(1);

        // Asserted directly rather than through WaitForLog, which would spend its whole deadline
        // proving a negative.
        LogMessages.ShouldNotContain(message =>
            message.Contains("Recording Auto-Stopped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADiscardedBrushAlsoCancelsTheWatchdog()
    {
        var orchestrator = Create(TimeSpan.FromMilliseconds(50));

        Dictate(TimeSpan.FromMilliseconds(20));
        await SettleAsync(orchestrator);

        Delay.Elapse();
        await SettleAsync(orchestrator);

        Capture.Stops.ShouldBe(1);
        Provider.Calls.ShouldBe(0);
    }

    // ---- Task 2.10: what is announced, and when ----

    [Fact]
    public async Task ASuccessfulDictationAnnouncesRecordingThenTranscribingThenIdle()
    {
        var orchestrator = Create();

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        States.ShouldBe([DictationState.Recording, DictationState.Transcribing, DictationState.Idle]);
    }

    [Fact]
    public async Task ADiscardedBrushAnnouncesRecordingThenIdleAndNeverTranscribing()
    {
        var orchestrator = Create(TimeSpan.FromMilliseconds(50));

        Dictate(TimeSpan.FromMilliseconds(20));
        await SettleAsync(orchestrator);

        States.ShouldBe([DictationState.Recording, DictationState.Idle]);
    }

    /// <summary>
    /// Announced from the pipeline's <c>finally</c>, so a dictation that failed returns the signal
    /// exactly as a successful one does — otherwise change 9's icon would stick on "transcribing".
    /// </summary>
    [Fact]
    public async Task AFailedDictationStillReturnsToIdle()
    {
        var orchestrator = Create();
        Provider.Failure = new InvalidOperationException("something nobody predicted");

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        States.ShouldBe([DictationState.Recording, DictationState.Transcribing, DictationState.Idle]);
        orchestrator.State.ShouldBe(DictationState.Idle);
    }

    /// <summary>
    /// Transcribing is announced when the stop is <em>claimed</em>, not when the capture device has
    /// finished closing, so the user's key press moves the icon immediately.
    /// </summary>
    [Fact]
    public async Task TranscribingIsAnnouncedBeforeTheCaptureHasFinishedClosing()
    {
        var orchestrator = Create();
        Capture.BlockStop();

        Dictate(TimeSpan.FromSeconds(2));

        (await WaitForAsync(() => States.Contains(DictationState.Transcribing))).ShouldBeTrue();

        States.ShouldBe([DictationState.Recording, DictationState.Transcribing]);
        Capture.Stops.ShouldBe(0);

        Capture.ReleaseStop();
        await SettleAsync(orchestrator);
    }
}
