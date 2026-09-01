namespace Pisum.Whisper.Core.Tests.Dictation;

using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Task 3.1 — the five places the pipeline tells the user something, and the policy deciding which
/// of them survive the preference being off.
/// </summary>
/// <remarks>
/// The real <c>NotificationService</c> sits in front of the recording presenter, so these exercise
/// the forced-versus-suppressible rule rather than asserting that a method was called.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class DictationNotificationTests : DictationTestBase
{
    // ---- the five sites ----

    [Fact]
    public async Task APressWhileTranscribingIsAnnounced()
    {
        var orchestrator = Create();
        Provider.Hang = true;

        Dictate(TimeSpan.FromSeconds(1));
        await Provider.Entered;

        Hotkeys.Press();

        Titles().ShouldContain("Transcription In Progress");
    }

    [Fact]
    public async Task ReachingTheMaximumIsAnnounced()
    {
        Configure(settings => settings.MaxRecordingDurationSecs = 42);
        var orchestrator = Create();

        Hotkeys.Press();
        (await WaitForAsync(() => Delay.Requested is not null)).ShouldBeTrue();

        Clock.Advance(TimeSpan.FromMinutes(10));
        Delay.Elapse();
        await SettleAsync(orchestrator);

        (await WaitForAsync(() => Titles().Contains("Recording Auto-Stopped"))).ShouldBeTrue();

        Message("Recording Auto-Stopped").ShouldContain("42");
    }

    [Fact]
    public void ACaptureThatWillNotStartIsShown()
    {
        var orchestrator = Create();
        Capture.StartFailure = new AudioException("No input device found.");

        Hotkeys.Press();

        Notifications.Presented.ShouldBe([("Recording Error", "No input device found.")]);

        // The state did not move, so the hotkey can still start the next recording.
        orchestrator.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task AFailedDictationIsShownWithItsCategoryTitle()
    {
        var orchestrator = Create();
        Provider.Failure = new TranscriptionException("The configured key was rejected.", ErrorCategory.Authentication);

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        (await WaitForAsync(() => Titles().Contains("Authentication Error"))).ShouldBeTrue();
        Message("Authentication Error").ShouldBe("The configured key was rejected.");
    }

    [Fact]
    public async Task ADeliveryThatOnlyReachedTheClipboardIsShown()
    {
        var orchestrator = Create();
        Output.Outcome = TextOutputOutcome.ClipboardOnly;

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        (await WaitForAsync(() => Titles().Contains("Paste Failed"))).ShouldBeTrue();
        Message("Paste Failed").ShouldContain("manually");
    }

    // ---- the policy ----

    /// <summary>
    /// Someone who silences status chatter still has to be told their key is rejected. The two
    /// informational sites are the only ones the preference reaches.
    /// </summary>
    [Fact]
    public async Task WithNotificationsOffAPressWhileTranscribingSaysNothing()
    {
        Configure(settings => settings.ShowTrayNotifications = false);
        var orchestrator = Create();
        Provider.Hang = true;

        Dictate(TimeSpan.FromSeconds(1));
        await Provider.Entered;

        Hotkeys.Press();

        WaitForLog("Transcription In Progress").ShouldBeTrue();
        Notifications.Count.ShouldBe(0);

        await orchestrator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void WithNotificationsOffACaptureFailureIsStillShown()
    {
        Configure(settings => settings.ShowTrayNotifications = false);
        Create();
        Capture.StartFailure = new AudioException("No input device found.");

        Hotkeys.Press();

        Notifications.Presented.ShouldBe([("Recording Error", "No input device found.")]);
    }

    [Fact]
    public async Task WithNotificationsOffTheWatchdogSaysNothing()
    {
        Configure(settings => settings.ShowTrayNotifications = false);
        var orchestrator = Create();

        Hotkeys.Press();
        (await WaitForAsync(() => Delay.Requested is not null)).ShouldBeTrue();

        Clock.Advance(TimeSpan.FromMinutes(10));
        Delay.Elapse();
        await SettleAsync(orchestrator);

        WaitForLog("Recording Auto-Stopped").ShouldBeTrue();
        Notifications.Count.ShouldBe(0);
    }

    /// <summary>
    /// Quitting is something the user asked for, so it is reported nowhere. The shutdown filter that
    /// separates it from an expired budget stays ahead of the notification.
    /// </summary>
    [Fact]
    public async Task ADictationAbandonedByShutdownSaysNothing()
    {
        var orchestrator = Create(transcriptionBudget: TimeSpan.FromMinutes(5));
        Provider.Hang = true;

        Dictate(TimeSpan.FromSeconds(2));
        await Provider.Entered;

        await orchestrator.StopAsync(CancellationToken.None);

        Notifications.Count.ShouldBe(0);
    }

    // ---- the disclosure rule ----

    /// <summary>
    /// A notification is drawn over whatever the user is presenting, so it is a wider disclosure
    /// than the log file the same rule already protects. Asserted on a failure path where a
    /// transcript genuinely exists, because that is the only place it could leak from.
    /// </summary>
    [Fact]
    public async Task NoNotificationCarriesTheTranscript()
    {
        const string spoken = "my password is hunter2 and the account number is 4417";

        var orchestrator = Create();
        Provider.Result = spoken;
        Output.Failure = new TextOutputException("the clipboard refused");

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        (await WaitForAsync(() => Notifications.Count > 0)).ShouldBeTrue("the failure should have been shown");

        foreach (var (title, message) in Notifications.Presented)
        {
            title.ShouldNotContain("hunter2");
            message.ShouldNotContain("hunter2");
            message.ShouldNotContain(spoken);
        }
    }

    [Fact]
    public async Task NoNotificationCarriesTheConfiguredKey()
    {
        const string key = "AIzaSyTOTALLYNOTAREALKEY";

        Configure(settings => settings.Providers.Add(new ProviderConfig
        {
            Id = "gemini-1",
            ApiKey = key,
            Enabled = true,
        }));

        var orchestrator = Create();
        Provider.Result = "the quick brown fox";
        Provider.Failure = new TranscriptionException(
            "The configured key was rejected.",
            ErrorCategory.Authentication);

        Dictate(TimeSpan.FromSeconds(2));
        await SettleAsync(orchestrator);

        (await WaitForAsync(() => Notifications.Count > 0)).ShouldBeTrue("the failure should have been shown");

        foreach (var (title, message) in Notifications.Presented)
        {
            title.ShouldNotContain(key);
            message.ShouldNotContain(key);
        }
    }

    private string[] Titles()
    {
        return [.. Notifications.Presented.Select(presented => presented.Title)];
    }

    private string Message(string title)
    {
        return Notifications.Presented.First(presented => presented.Title == title).Message;
    }
}
