namespace Pisum.Whisper.Core.Tests.Dictation;

using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Tasks 4.1, 4.2 and 4.3 — the failure vocabulary. The title comes from what failed, never from
/// matching the text of a message.
/// </summary>
[TestClass]
public sealed class DictationFailureTests
{
    [TestMethod]
    [DataRow(ErrorCategory.Configuration, "Configuration Error")]
    [DataRow(ErrorCategory.Network, "Network Error")]
    [DataRow(ErrorCategory.Authentication, "Authentication Error")]
    [DataRow(ErrorCategory.RateLimit, "Rate Limit Error")]
    [DataRow(ErrorCategory.Transcription, "Transcription Error")]
    public void EveryTranscriptionCategoryHasItsOwnTitle(ErrorCategory category, string expected)
    {
        var (title, message) = DictationFailure.Describe(
            new TranscriptionException("the provider said no", category));

        title.ShouldBe(expected);
        message.ShouldBe("the provider said no");
    }

    [TestMethod]
    public void AnAudioFailureIsARecordingError()
    {
        var (title, message) = DictationFailure.Describe(new AudioException("No input device found"));

        title.ShouldBe("Recording Error");
        message.ShouldBe("No input device found");
    }

    [TestMethod]
    public void AClipboardFailureIsAnOutputError()
    {
        var (title, message) = DictationFailure.Describe(new TextOutputException("the clipboard refused"));

        title.ShouldBe("Output Error");
        message.ShouldBe("the clipboard refused");
    }

    /// <summary>
    /// An exception nobody anticipated still produces something to show. The alternative is a
    /// dictation that fails with nothing said at all.
    /// </summary>
    [TestMethod]
    public void AnUnrecognisedFailureIsStillDescribed()
    {
        var (title, message) = DictationFailure.Describe(new BadImageFormatException("internal detail"));

        title.ShouldBe("Unexpected Error");
        message.ShouldNotBeNullOrWhiteSpace();

        // The internal detail is not what the user is shown; the log is where that lives.
        message.ShouldNotContain("internal detail");
    }

    // ---- Task 4.2: the two cancellations ----

    [TestMethod]
    public void AnExpiredBudgetIsATranscriptionError()
    {
        var (title, message) = DictationFailure.Describe(new OperationCanceledException());

        title.ShouldBe("Transcription Error");
        message.ShouldBe(DictationFailure.BudgetExpiredMessage);
    }

    /// <summary>
    /// A settings failure has no arm of its own — it cannot reach the pipeline, because the store is
    /// read rather than loaded here — so it lands on the catch-all rather than being mis-titled.
    /// </summary>
    [TestMethod]
    public void AFailureFromAnotherCapabilityFallsToTheCatchAll()
    {
        var (title, _) = DictationFailure.Describe(new SettingsException("the file is unreadable"));

        title.ShouldBe("Unexpected Error");
    }

    // ---- Task 4.3: the deferred macOS branch ----

    /// <summary>
    /// The reference adds a macOS-only "Microphone Access Required" title by substring-matching
    /// "No input device" (<c>hotkey/manager.rs:249-256</c>). That is the mechanism
    /// <see cref="ErrorCategory"/> exists to avoid, and it is a guess besides: spike S2 passed on the
    /// M4 with the microphone accessible, so nobody has observed what a refused grant looks like.
    /// The branch is deliberately absent, and this test is what would fail if someone added it.
    /// </summary>
    [TestMethod]
    public void AMissingInputDeviceIsNotSpecialCased()
    {
        var (title, _) = DictationFailure.Describe(new AudioException("No input device found"));

        title.ShouldBe("Recording Error");
        title.ShouldNotBe("Microphone Access Required");
    }
}

/// <summary>
/// Task 4.2's other half, which lives in the orchestrator rather than in the mapping: quitting
/// produces the same exception as an expired budget and must say nothing at all.
/// </summary>
[TestClass]
public sealed class DictationShutdownSilenceTests : DictationTestBase
{
    [TestMethod]
    public async Task QuittingDuringATranscriptionDescribesNoFailure()
    {
        var orchestrator = Create();
        Provider.Hang = true;

        Dictate(TimeSpan.FromSeconds(2));
        await Provider.Entered;

        await orchestrator.StopAsync(CancellationToken.None);

        LogMessages.ShouldNotContain(message =>
            message.Contains("The dictation failed", StringComparison.Ordinal));

        LogMessages.ShouldNotContain(message =>
            message.Contains(DictationFailureTitles.TranscriptionError, StringComparison.Ordinal));
    }
}
