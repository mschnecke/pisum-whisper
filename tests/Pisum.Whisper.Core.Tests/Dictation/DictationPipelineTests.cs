namespace Pisum.Whisper.Core.Tests.Dictation;

using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Tasks 3.1 to 3.5 — what the pipeline sends where, the two cancellation tokens, the degraded
/// delivery, and the guarantee that no failure wedges the state machine.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class DictationPipelineTests : DictationTestBase
{
    // ---- Task 3.1: what the encoder is handed ----

    [Fact]
    public async Task TheEncoderIsHandedTheCaptureRateAndTheConfiguredFormat()
    {
        Configure(settings => settings.AudioFormat = AudioFormat.Wav);
        var orchestrator = Create();

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Encoder.SampleRate.ShouldBe(IAudioCapture.SampleRate);
        Encoder.Preferred.ShouldBe(AudioFormat.Wav);
    }

    /// <summary>
    /// Settings are read per dictation and never cached, following <c>GeminiProviderPool</c>: there
    /// is no change subscription here and no rebuild step, because <c>SettingsStore.Current</c> is
    /// already authoritative.
    /// </summary>
    [Fact]
    public async Task ASettingsChangeBetweenDictationsIsPickedUp()
    {
        Configure(settings => settings.AudioFormat = AudioFormat.Opus);
        var orchestrator = Create();

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);
        Encoder.Preferred.ShouldBe(AudioFormat.Opus);

        Configure(settings => settings.AudioFormat = AudioFormat.Wav);

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);
        Encoder.Preferred.ShouldBe(AudioFormat.Wav);
    }

    [Fact]
    public async Task TheEncodedAudioIsWhatReachesTheProvider()
    {
        var orchestrator = Create();
        Encoder.Result = new EncodedAudio([9, 8, 7], EncodedAudio.WavMimeType, AudioFormat.Wav);

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Provider.Audio.ShouldBe(Encoder.Result);
    }

    // ---- Task 3.2: the active preset's prompt ----

    [Fact]
    public async Task TheActivePresetsSystemPromptIsSent()
    {
        var orchestrator = Create();
        var active = Settings.Current.Presets.First(preset => preset.Id == Settings.Current.ActivePresetId);

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Provider.SystemPrompt.ShouldBe(active.SystemPrompt);
    }

    [Fact]
    public async Task ChangingTheActivePresetChangesThePromptSent()
    {
        var orchestrator = Create();

        Settings.SavePreset(new Preset
        {
            Id = "custom",
            Name = "Custom",
            SystemPrompt = "Write it all in capitals.",
        });

        Settings.SetActivePreset("custom");

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Provider.SystemPrompt.ShouldBe("Write it all in capitals.");
    }

    // ---- Task 3.3: the two tokens ----

    /// <summary>
    /// The 60 s client timeout is per request; three attempts across N keys multiply it into
    /// minutes. The budget is what keeps a hung upload from holding the hotkey shut with nothing
    /// said.
    /// </summary>
    [Fact]
    public async Task ATranscriptionThatOverrunsTheBudgetIsAbandoned()
    {
        var orchestrator = Create(transcriptionBudget: TimeSpan.FromMilliseconds(50));
        Provider.Hang = true;

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Output.Calls.ShouldBe(0);
        orchestrator.State.ShouldBe(DictationState.Idle);
        WaitForLog(DictationFailureTitles.TranscriptionError).ShouldBeTrue();
    }

    /// <summary>
    /// The delivery is deliberately outside the budget: it spends more than a second waiting before
    /// its restore by design, and an expired transcription clock must not cut that short.
    /// </summary>
    [Fact]
    public async Task AnExpiredBudgetDoesNotCancelADeliveryAlreadyRunning()
    {
        var orchestrator = Create(transcriptionBudget: TimeSpan.FromMilliseconds(50));
        Output.Block();

        Dictate(TimeSpan.FromSeconds(2));
        await Output.Entered;

        // Well past the budget, which by now has expired against a transcription that already
        // finished. The delivery must not have been touched by it.
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Output.Release();
        await SettleAsync(orchestrator);

        Output.WasCancelled.ShouldBeFalse();
        Output.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ATranscriptionInsideTheBudgetIsUnaffected()
    {
        var orchestrator = Create(transcriptionBudget: TimeSpan.FromSeconds(30));

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Provider.Calls.ShouldBe(1);
        Output.Calls.ShouldBe(1);
    }

    // ---- Task 3.4: a degraded delivery is not a failure ----

    [Fact]
    public async Task AClipboardOnlyDeliveryIsNotAFailure()
    {
        var orchestrator = Create();
        Output.Outcome = TextOutputOutcome.ClipboardOnly;

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        orchestrator.State.ShouldBe(DictationState.Idle);
        LogMessages.ShouldNotContain(message =>
            message.Contains("The dictation failed", StringComparison.Ordinal));
    }

    /// <summary>
    /// The manual-paste message is recorded once — the spec requires the user to be told, and in
    /// this change the log is the only place to tell them. What is <em>not</em> repeated is
    /// <c>TextOutput</c>'s own diagnosis of why the paste did not happen.
    /// </summary>
    [Fact]
    public async Task AClipboardOnlyDeliveryRecordsTheManualPasteMessageOnce()
    {
        var orchestrator = Create();
        Output.Outcome = TextOutputOutcome.ClipboardOnly;

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        WaitForLog("Paste Failed").ShouldBeTrue();
        LogMessages.Count(message => message.Contains("Paste Failed", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public async Task APastedDeliverySaysNothingToTheUser()
    {
        var orchestrator = Create();
        Output.Outcome = TextOutputOutcome.Pasted;

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        LogMessages.ShouldNotContain(message =>
            message.Contains("Paste Failed", StringComparison.Ordinal));
    }

    // ---- Task 3.5: nothing wedges the state machine ----

    [Fact]
    public async Task AFailureInAnyStageReturnsTheStateToIdle()
    {
        foreach (var stage in Stages())
        {
            var orchestrator = Create();
            stage();

            Dictate(TimeSpan.FromSeconds(2));
            await SettleAsync(orchestrator);

            orchestrator.State.ShouldBe(DictationState.Idle);

            Reset();
            orchestrator.Dispose();
        }
    }

    [Fact]
    public async Task ADictationCanFollowAFailedOne()
    {
        var orchestrator = Create();
        Provider.Failure = new TranscriptionException("nope", ErrorCategory.Network);

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Provider.Failure = null;

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Capture.Starts.ShouldBe(2);
        Output.Calls.ShouldBe(1);
    }

    /// <summary>
    /// The .NET hazard the reference does not have: an exception escaping the pipeline task becomes
    /// an unobserved task exception, which does not crash the process. It would vanish, leave the
    /// state at Transcribing for ever, and the hotkey would answer "Transcription In Progress" until
    /// the application was restarted.
    /// </summary>
    [Fact]
    public async Task AnUnrecognisedFailureIsStillDescribed()
    {
        var orchestrator = Create();
        Output.Failure = new BadImageFormatException("nobody predicted this");

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        orchestrator.State.ShouldBe(DictationState.Idle);
        WaitForLog(DictationFailureTitles.UnexpectedError).ShouldBeTrue();
    }

    /// <summary>
    /// A state subscriber runs arbitrary code, and the announcement is the pipeline task's first
    /// act. Before this was guarded, a throwing subscriber meant <c>RunAsync</c> never ran at all:
    /// the capture was never closed, the private capturing flag stayed set, and the state sat at
    /// Transcribing for ever with the hotkey answering "Transcription In Progress" until restart.
    /// </summary>
    [Fact]
    public async Task AThrowingStateSubscriberDoesNotWedgeTheDictation()
    {
        var orchestrator = Create();
        orchestrator.StateChanged += (_, _) => throw new InvalidOperationException("a subscriber went wrong");

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        // The dictation still ran end to end, and the device was closed.
        Capture.Stops.ShouldBe(1);
        Provider.Calls.ShouldBe(1);
        Output.Calls.ShouldBe(1);
        orchestrator.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task ADictationCanFollowOneWhoseSubscriberThrew()
    {
        var orchestrator = Create();
        orchestrator.StateChanged += (_, _) => throw new InvalidOperationException("a subscriber went wrong");

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        // Wedging would have made the second press report "Transcription In Progress" instead.
        Capture.Starts.ShouldBe(2);
        Provider.Calls.ShouldBe(2);
    }

    private IEnumerable<Action> Stages()
    {
        yield return () => Capture.StopFailure = new InvalidOperationException("capture");
        yield return () => Encoder.Failure = new AudioException("encode");
        yield return () => Provider.Failure = new TranscriptionException("transcribe", ErrorCategory.Network);
        yield return () => Output.Failure = new TextOutputException("deliver");
    }

    private void Reset()
    {
        Capture.StopFailure = null;
        Encoder.Failure = null;
        Provider.Failure = null;
        Output.Failure = null;
    }
}
