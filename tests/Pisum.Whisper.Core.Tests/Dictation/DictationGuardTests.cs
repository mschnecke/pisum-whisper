namespace Pisum.Whisper.Core.Tests.Dictation;

using Pisum.Whisper.Core.Dictation;
using Shouldly;

/// <summary>
/// Tasks 2.1, 2.5, 2.6, 2.7 and 2.9 — the dispatch-thread rule, the two duration rules, the two
/// concurrency guards, and the atomic claim that keeps three callers from ending one recording.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class DictationGuardTests : DictationTestBase
{
    // ---- Task 2.1: nothing but a state transition on the dispatch thread ----

    /// <summary>
    /// The regression test for blocking change 6's dispatch loop. <c>GlobalHotkeyService</c> raises
    /// the edges synchronously from its channel read loop, so a handler that awaited the pipeline
    /// would stop the very next edge being delivered — and in hold-to-record that edge is the
    /// release which ends the recording.
    /// </summary>
    [Fact]
    public async Task TheReleaseHandlerReturnsBeforeThePipelineFinishes()
    {
        var orchestrator = Create();
        Capture.BlockStop();

        Dictate(TimeSpan.FromSeconds(1));

        // Control is back here while the pipeline is still stuck inside StopAsync.
        orchestrator.State.ShouldBe(DictationState.Transcribing);
        Output.Calls.ShouldBe(0);

        Capture.ReleaseStop();
        await SettleAsync(orchestrator);

        Output.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task AFurtherPressIsHandledWhileATranscriptionIsStillRunning()
    {
        var orchestrator = Create();
        Provider.Hang = true;

        Dictate(TimeSpan.FromSeconds(1));
        await Provider.Entered;

        // Would deadlock, or block for the length of the transcription, if the handler awaited.
        Hotkeys.Press();

        orchestrator.State.ShouldBe(DictationState.Transcribing);
        WaitForLog("Transcription In Progress").ShouldBeTrue();
    }

    // ---- Task 2.5: the 50 ms minimum ----

    [Fact]
    public async Task ABrushOfTheHotkeyIsDiscardedInSilence()
    {
        var orchestrator = Create(TimeSpan.FromMilliseconds(50));

        Dictate(TimeSpan.FromMilliseconds(20));
        await SettleAsync(orchestrator);

        Capture.Stops.ShouldBe(1);
        Encoder.Calls.ShouldBe(0);
        Provider.Calls.ShouldBe(0);
        Output.Calls.ShouldBe(0);

        // Silent means silent: an accident must not raise an error either.
        States.ShouldNotContain(DictationState.Transcribing);
        LogMessages.ShouldNotContain(message => message.Contains("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APressOverTheMinimumIsTranscribed()
    {
        var orchestrator = Create(TimeSpan.FromMilliseconds(50));

        Dictate(TimeSpan.FromMilliseconds(80));
        await SettleAsync(orchestrator);

        Provider.Calls.ShouldBe(1);
    }

    /// <summary>
    /// The device is still closing when a discarded brush announces Idle, so the next press must not
    /// reopen it — <c>MiniAudioCapture.Start</c> throws on a second open.
    /// </summary>
    [Fact]
    public async Task APressWhileADiscardedRecordingIsStillClosingDoesNotReopenTheDevice()
    {
        var orchestrator = Create(TimeSpan.FromMilliseconds(50));
        Capture.BlockStop();

        Dictate(TimeSpan.FromMilliseconds(20));
        orchestrator.State.ShouldBe(DictationState.Idle);

        Hotkeys.Press();

        Capture.Starts.ShouldBe(1);

        Capture.ReleaseStop();
        await SettleAsync(orchestrator);
    }

    // ---- Task 2.6: an empty capture over the minimum is a fault ----

    [Fact]
    public async Task AnEmptyCaptureOverTheMinimumIsReportedAsARecordingError()
    {
        var orchestrator = Create(TimeSpan.FromMilliseconds(50));
        Capture.Samples = [];

        Dictate(TimeSpan.FromSeconds(3));
        await SettleAsync(orchestrator);

        Encoder.Calls.ShouldBe(0);
        Provider.Calls.ShouldBe(0);
        WaitForLog(DictationFailureTitles.RecordingError).ShouldBeTrue();
    }

    [Fact]
    public async Task AnEmptyCaptureUnderTheMinimumIsStillJustABrush()
    {
        var orchestrator = Create(TimeSpan.FromMilliseconds(50));
        Capture.Samples = [];

        Dictate(TimeSpan.FromMilliseconds(20));
        await SettleAsync(orchestrator);

        LogMessages.ShouldNotContain(message =>
            message.Contains(DictationFailureTitles.RecordingError, StringComparison.Ordinal));
    }

    // ---- Task 2.7: the two concurrency guards ----

    [Fact]
    public async Task APressWhileRecordingIsIgnoredWithoutAWord()
    {
        var orchestrator = Create();

        Hotkeys.Press();
        Hotkeys.Press();

        Capture.Starts.ShouldBe(1);
        LogMessages.ShouldNotContain(message =>
            message.Contains("Transcription In Progress", StringComparison.Ordinal));

        Clock.Advance(TimeSpan.FromSeconds(1));
        Hotkeys.Release();
        await SettleAsync(orchestrator);
    }

    [Fact]
    public async Task APressWhileTranscribingStartsNothingAndSaysSo()
    {
        var orchestrator = Create();
        Provider.Hang = true;

        Dictate(TimeSpan.FromSeconds(1));
        await Provider.Entered;

        Hotkeys.Press();

        Capture.Starts.ShouldBe(1);
        WaitForLog("Transcription In Progress").ShouldBeTrue();
        orchestrator.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public async Task APressAfterADictationFinishesStartsANewRecording()
    {
        var orchestrator = Create();

        Dictate(TimeSpan.FromSeconds(1));
        await SettleAsync(orchestrator);

        Hotkeys.Press();

        orchestrator.State.ShouldBe(DictationState.Recording);
        Capture.Starts.ShouldBe(2);

        Clock.Advance(TimeSpan.FromSeconds(1));
        Hotkeys.Release();
        await SettleAsync(orchestrator);
    }

    // ---- Task 2.9: the atomic claim ----

    [Fact]
    public async Task TheWatchdogAndAReleaseTogetherStopTheRecordingOnce()
    {
        var orchestrator = Create();

        Hotkeys.Press();
        Clock.Advance(TimeSpan.FromSeconds(1));

        // Both claimants arrive at once. Exactly one may run the pipeline over the one capture.
        Parallel.Invoke(() => Delay.Elapse(), () => Hotkeys.Release());

        await SettleAsync(orchestrator);

        Capture.Stops.ShouldBe(1);
        Encoder.Calls.ShouldBe(1);
        Provider.Calls.ShouldBe(1);
        Output.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task AReleaseAfterTheWatchdogHasClaimedIsANoOp()
    {
        var orchestrator = Create();

        Hotkeys.Press();
        Clock.Advance(TimeSpan.FromSeconds(1));
        Delay.Elapse();
        await SettleAsync(orchestrator);

        Hotkeys.Release();
        await SettleAsync(orchestrator);

        Capture.Stops.ShouldBe(1);
        Provider.Calls.ShouldBe(1);
    }
}

/// <summary>The titles asserted above, kept in one place rather than spelled out at each use.</summary>
internal static class DictationFailureTitles
{
    public const string RecordingError = "Recording Error";

    public const string TranscriptionError = "Transcription Error";

    public const string OutputError = "Output Error";

    public const string UnexpectedError = "Unexpected Error";
}
