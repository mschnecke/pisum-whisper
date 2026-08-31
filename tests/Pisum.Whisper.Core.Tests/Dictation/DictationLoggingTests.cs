namespace Pisum.Whisper.Core.Tests.Dictation;

using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>
/// Task 5.3 — the logging rules, at the most verbose level there is. This component sees the user's
/// speech and writes none of it down, and it sees no keys at all.
/// </summary>
public sealed class DictationLoggingTests : DictationTestBase
{
    private const string Spoken = "my password is hunter2 and the account number is 4417";

    [Fact]
    public async Task TheTranscriptIsNeverLogged()
    {
        var orchestrator = Create();
        Provider.Result = Spoken;

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        Output.Transcript.ShouldBe(Spoken);

        foreach (var message in LogMessages)
        {
            message.ShouldNotContain(Spoken);
            message.ShouldNotContain("hunter2");
        }

        // Past the rendered message and into the attached properties, where a structured sink would
        // otherwise carry it into the log file regardless of the template.
        foreach (var logEvent in LogEvents)
        {
            foreach (var property in logEvent.Properties.Values)
            {
                property.ToString().ShouldNotContain("hunter2");
            }
        }
    }

    [Fact]
    public async Task AFailedDictationDoesNotLogTheTranscriptEither()
    {
        var orchestrator = Create();
        Provider.Result = Spoken;
        Output.Failure = new InvalidOperationException("the delivery went wrong");

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        foreach (var message in LogMessages)
        {
            message.ShouldNotContain("hunter2");
        }
    }

    /// <summary>
    /// What the log does carry: enough to tell a dictation that happened from one that did not.
    /// </summary>
    [Fact]
    public async Task TheRecordingIsDescribedByDurationAndSampleCount()
    {
        var orchestrator = Create();
        Capture.Samples = [0.1f, 0.2f, 0.3f, 0.4f];

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        WaitForLog("Transcribing a 2.0 s recording of 4 samples").ShouldBeTrue();
    }

    /// <summary>
    /// Change 6's dispatch loop already writes one line per edge at Information. A second line here
    /// would double the most common entries in the file — and this component has no business writing
    /// anything about a key, since it is the one that turns them into recordings.
    /// </summary>
    [Fact]
    public async Task NoKeyIsEverMentioned()
    {
        var orchestrator = Create();

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        foreach (var message in LogMessages)
        {
            message.ShouldNotContain("KeyCode");
            message.ShouldNotContain("Vc");
            message.ShouldNotContain("Hotkey ");
        }
    }

    /// <summary>
    /// The API key lives in settings, which this component reads on every dictation. It reads the
    /// audio format and the active preset from the same object, so the rule is worth an assertion.
    /// </summary>
    [Fact]
    public async Task NoApiKeyIsEverLogged()
    {
        Configure(settings => settings.Providers.Add(new ProviderConfig
        {
            Id = "gemini-1",
            ApiKey = "AIzaSyTOTALLYNOTAREALKEY",
            Enabled = true,
        }));

        var orchestrator = Create();

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        foreach (var message in LogMessages)
        {
            message.ShouldNotContain("AIzaSyTOTALLYNOTAREALKEY");
        }
    }
}
