namespace Pisum.Whisper.Core.Tests.Dictation;

using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>
/// Tasks 2.2, 2.3 and 2.4 — the two recording modes and the debounce that guards one of them.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class DictationModeTests : DictationTestBase
{
    // ---- Task 2.2: hold-to-record ----

    [Fact]
    public async Task HoldToRecord_RunsTheWholePipelineOnce()
    {
        var orchestrator = Create();

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Capture.Starts.ShouldBe(1);
        Capture.Stops.ShouldBe(1);
        Encoder.Calls.ShouldBe(1);
        Provider.Calls.ShouldBe(1);
        Output.Calls.ShouldBe(1);
    }

    /// <summary>
    /// The order of the five stages, asserted through the data rather than through a call log:
    /// each stage received exactly what the one before it produced, which cannot happen in any
    /// other sequence.
    /// </summary>
    [Fact]
    public async Task HoldToRecord_FlowsTheRecordingThroughEveryStage()
    {
        var orchestrator = Create();
        Capture.Samples = [0.5f, 0.6f];
        Provider.Result = Transcript;

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Encoder.Samples.ShouldBe(Capture.Samples);
        Provider.Audio.ShouldBe(Encoder.Result);
        Output.Transcript.ShouldBe(Transcript);
    }

    [Fact]
    public async Task HoldToRecord_PressAloneStartsARecordingAndNothingElse()
    {
        var orchestrator = Create();

        Hotkeys.Press();

        orchestrator.State.ShouldBe(DictationState.Recording);
        Capture.Starts.ShouldBe(1);
        Provider.Calls.ShouldBe(0);

        // Leave the fixture at rest so teardown does not race the pipeline.
        Clock.Advance(TimeSpan.FromSeconds(1));
        Hotkeys.Release();
        await SettleAsync(orchestrator);
    }

    // ---- Task 2.3: toggle ----

    [Fact]
    public void Toggle_TheFirstPressStartsRecording()
    {
        Configure(settings => settings.RecordingMode = RecordingMode.Toggle);
        var orchestrator = Create();

        Hotkeys.Press();

        orchestrator.State.ShouldBe(DictationState.Recording);
        Capture.Starts.ShouldBe(1);
    }

    [Fact]
    public void Toggle_TheReleaseIsIgnoredEntirely()
    {
        Configure(settings => settings.RecordingMode = RecordingMode.Toggle);
        var orchestrator = Create();

        Hotkeys.Press();
        Clock.Advance(TimeSpan.FromSeconds(1));
        Hotkeys.Release();

        orchestrator.State.ShouldBe(DictationState.Recording);
        Capture.Stops.ShouldBe(0);
        Provider.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Toggle_ALaterPressStopsAndTranscribes()
    {
        Configure(settings => settings.RecordingMode = RecordingMode.Toggle);
        var orchestrator = Create();

        Hotkeys.Press();
        Clock.Advance(TimeSpan.FromSeconds(1));
        Hotkeys.Press();
        await SettleAsync(orchestrator);

        Capture.Stops.ShouldBe(1);
        Provider.Calls.ShouldBe(1);
        Output.Calls.ShouldBe(1);
    }

    // ---- Task 2.4: the debounce ----

    [Fact]
    public void Toggle_ASecondPressInsideTheDebounceWindowIsIgnored()
    {
        Configure(settings => settings.RecordingMode = RecordingMode.Toggle);
        var orchestrator = Create(debounceWindow: TimeSpan.FromMilliseconds(200));

        Hotkeys.Press();
        Clock.Advance(TimeSpan.FromMilliseconds(120));
        Hotkeys.Press();

        // The fumbled second tap changed nothing: still recording, nothing transcribed.
        orchestrator.State.ShouldBe(DictationState.Recording);
        Capture.Stops.ShouldBe(0);
        Provider.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Toggle_ASecondPressOutsideTheDebounceWindowIsActedOn()
    {
        Configure(settings => settings.RecordingMode = RecordingMode.Toggle);
        var orchestrator = Create(debounceWindow: TimeSpan.FromMilliseconds(200));

        Hotkeys.Press();
        Clock.Advance(TimeSpan.FromMilliseconds(260));
        Hotkeys.Press();
        await SettleAsync(orchestrator);

        Capture.Stops.ShouldBe(1);
        Provider.Calls.ShouldBe(1);
    }

    /// <summary>
    /// The debounce is toggle's alone. In hold-to-record the two edges are not two presses, so
    /// applying it there would silently drop a genuine second dictation.
    /// </summary>
    [Fact]
    public async Task HoldToRecord_IsNotDebounced()
    {
        var orchestrator = Create(debounceWindow: TimeSpan.FromSeconds(30));

        Dictate(TimeSpan.FromMilliseconds(600));
        await SettleAsync(orchestrator);

        Dictate(TimeSpan.FromMilliseconds(600));
        await SettleAsync(orchestrator);

        Capture.Starts.ShouldBe(2);
        Provider.Calls.ShouldBe(2);
    }
}
