namespace Pisum.Whisper.App.Tests;

using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Logging;
using Shouldly;

/// <summary>
/// Task 5.1 — the two conditions that leave the application running but degraded, reported once a
/// surface exists to report them on.
/// </summary>
/// <remarks>
/// <para>
/// Driven through <see cref="App.ReportStartupConditions"/> rather than through
/// <c>App.OnFrameworkInitializationCompleted</c>, for the same reason <see cref="FirstLaunchTests"/>
/// is: constructing an <see cref="App"/> opens tray assets and registers a native tray icon, and a
/// headless platform provides neither.
/// </para>
/// <para>
/// <c>Unit</c> rather than <c>Integration</c>: the hotkey service is a fake, and the unusable
/// <see cref="LogDirectory"/> is made unusable by a path the operating system rejects without being
/// asked to touch a disk — so nothing here creates a file, a directory or a temp path.
/// </para>
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class StartupConditionsTests
{
    private const string LogsPath = @"C:\nowhere\pisum-whisper\logs";

    private readonly RecordingNotificationService _notifications = new();

    private readonly FakeAvailabilityHotkeyService _hotkeys = new();

    [Fact]
    public void NothingIsSaidWhenBothAreHealthy()
    {
        App.ReportStartupConditions(UsableLogs(), _hotkeys, _notifications);

        _notifications.Forced.ShouldBeEmpty();
        _notifications.Informational.ShouldBeEmpty();
    }

    [Fact]
    public void AnUnusableLogDirectoryIsReportedOnce()
    {
        var logs = UnusableLogs();

        App.ReportStartupConditions(logs, _hotkeys, _notifications);

        _notifications.Forced.Count.ShouldBe(1);
        _notifications.Forced[0].Title.ShouldBe("Logging Unavailable");

        // The path and the reason both, because the log that would have carried them is the thing
        // that is missing.
        _notifications.Forced[0].Message.ShouldContain(LogsPath);
        _notifications.Forced[0].Message.ShouldContain(logs.FailureReason!);
    }

    [Theory]
    [InlineData(HotkeyAvailability.PermissionNotGranted, "has not been granted")]
    [InlineData(HotkeyAvailability.PermissionRevoked, "was withdrawn")]
    [InlineData(HotkeyAvailability.Failed, "Check the log")]
    public void AnUnobservedBindingIsReportedWithItsOwnRemedy(HotkeyAvailability availability, string expected)
    {
        _hotkeys.Availability = availability;

        App.ReportStartupConditions(UsableLogs(), _hotkeys, _notifications);

        _notifications.Forced.Count.ShouldBe(1);
        _notifications.Forced[0].Title.ShouldBe("Hotkey Unavailable");

        // The two permission states are kept apart because their remedies differ, which is the whole
        // reason HotkeyAvailability distinguishes them.
        _notifications.Forced[0].Message.ShouldContain(expected);
    }

    [Fact]
    public void BothDegradedConditionsAreReported()
    {
        _hotkeys.Availability = HotkeyAvailability.PermissionNotGranted;

        App.ReportStartupConditions(UnusableLogs(), _hotkeys, _notifications);

        _notifications.Forced.Select(forced => forced.Title)
            .ShouldBe(["Logging Unavailable", "Hotkey Unavailable"]);
    }

    /// <summary>
    /// A start that timed out and then succeeded reports itself, so the seed and the event genuinely
    /// do carry the same value — and the user must be told once.
    /// </summary>
    [Fact]
    public void TheEventRepeatingAValueAlreadyReportedSaysNothingAgain()
    {
        _hotkeys.Availability = HotkeyAvailability.PermissionRevoked;

        App.ReportStartupConditions(UsableLogs(), _hotkeys, _notifications);
        _notifications.Forced.Count.ShouldBe(1);

        _hotkeys.Publish(HotkeyAvailability.PermissionRevoked);

        _notifications.Forced.Count.ShouldBe(1);
    }

    /// <summary>
    /// The case with no mechanism at all before this change: access withdrawn long after startup, in
    /// <c>RunHookAsync</c>'s catch, reaching nothing.
    /// </summary>
    [Fact]
    public void AChangeToANewValueIsReported()
    {
        App.ReportStartupConditions(UsableLogs(), _hotkeys, _notifications);
        _notifications.Forced.ShouldBeEmpty();

        _hotkeys.Publish(HotkeyAvailability.PermissionRevoked);

        _notifications.Forced.Count.ShouldBe(1);
        _notifications.Forced[0].Title.ShouldBe("Hotkey Unavailable");
    }

    /// <summary>
    /// Observation that could not be confirmed at startup but begins afterwards is not a failure, so
    /// nothing is said when it recovers.
    /// </summary>
    [Fact]
    public void ObservationBeginningLateIsNotReportedAsAFailure()
    {
        App.ReportStartupConditions(UsableLogs(), _hotkeys, _notifications);

        _hotkeys.Publish(HotkeyAvailability.Available);

        _notifications.Forced.ShouldBeEmpty();
    }

    /// <summary>
    /// Subscribing before reading is what stops a transition that lands between the two from being
    /// lost — the same reasoning as the tray icon's seeded <c>ApplyState</c>. The seed then finds the
    /// value already reported and says nothing.
    /// </summary>
    [Fact]
    public void ATransitionThatLandsBeforeTheSeedIsReportedExactlyOnce()
    {
        _hotkeys.PublishOnSubscribe = HotkeyAvailability.PermissionRevoked;

        App.ReportStartupConditions(UsableLogs(), _hotkeys, _notifications);

        _notifications.Forced.Count.ShouldBe(1);
        _notifications.Forced[0].Title.ShouldBe("Hotkey Unavailable");
    }

    /// <summary>
    /// Both are forced, so both survive a user who has turned status notifications off. Someone who
    /// silenced chatter has not asked to be kept from knowing that their hotkey does not work.
    /// </summary>
    [Fact]
    public void BothAreForcedRatherThanSuppressible()
    {
        _hotkeys.Availability = HotkeyAvailability.Failed;

        App.ReportStartupConditions(UnusableLogs(), _hotkeys, _notifications);

        _notifications.Forced.Count.ShouldBe(2);
        _notifications.Informational.ShouldBeEmpty(
            "NotifyInformation is the half ShowTrayNotifications silences, and neither of these may go through it");
    }

    /// <summary>TryCreate is never called, so the reason stays null and nothing is touched on disk.</summary>
    private static LogDirectory UsableLogs()
    {
        return new LogDirectory(LogsPath);
    }

    /// <summary>
    /// A directory that could not be created, without creating anything.
    /// </summary>
    /// <remarks>
    /// The trailing null character is rejected by path validation before any file system is asked
    /// anything, and <c>ArgumentException</c> is already one of the four <c>TryCreate</c> catches. So
    /// this goes through the real <c>TryCreate</c> rather than reaching past it into the private
    /// setter, and still costs no I/O.
    /// </remarks>
    private static LogDirectory UnusableLogs()
    {
        var logs = new LogDirectory(LogsPath + "\0");

        logs.TryCreate().ShouldNotBeNull("the path has to be genuinely unusable for this to mean anything");

        return logs;
    }

    /// <summary>
    /// Publishes availability on demand, which is what the real service does from three places, none
    /// of them the hook thread.
    /// </summary>
    private sealed class FakeAvailabilityHotkeyService : IGlobalHotkeyService
    {
        private EventHandler<HotkeyAvailability>? _availabilityChanged;

        // Never raised here: this fake exists for the availability half alone.
        public event EventHandler? Pressed
        {
            add { }
            remove { }
        }

        public event EventHandler? Released
        {
            add { }
            remove { }
        }

        public event EventHandler<HotkeyAvailability>? AvailabilityChanged
        {
            add
            {
                _availabilityChanged += value;

                // The narrow window the subscribe-then-read ordering exists for: a transition that
                // lands after the subscription and before the seed is read.
                if (PublishOnSubscribe is { } availability)
                {
                    PublishOnSubscribe = null;
                    Publish(availability);
                }
            }

            remove => _availabilityChanged -= value;
        }

        public HotkeyAvailability Availability { get; set; } = HotkeyAvailability.Available;

        /// <summary>When set, a transition lands the instant something subscribes.</summary>
        public HotkeyAvailability? PublishOnSubscribe { get; set; }

        public HotkeyChord Chord => HotkeyChord.Default;

        public Task<HotkeyCapture> CaptureAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(HotkeyCapture.Cancelled);
        }

        public void Publish(HotkeyAvailability availability)
        {
            Availability = availability;
            _availabilityChanged?.Invoke(this, availability);
        }
    }
}
