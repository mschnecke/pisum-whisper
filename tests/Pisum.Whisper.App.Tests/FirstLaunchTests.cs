namespace Pisum.Whisper.App.Tests;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.App.Tests.Settings;
using Pisum.Whisper.App.Tests.Views;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Notifications;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Shell;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Task 5.1 — what a new user gets: a message saying the application is running and needs a
/// provider, and the window that message is pointing at.
/// </summary>
/// <remarks>
/// The flow is driven through <see cref="App.ShowFirstLaunch"/> rather than through
/// <c>App.OnFrameworkInitializationCompleted</c>, because constructing an <see cref="App"/> opens
/// tray assets and registers a native tray icon, and a headless platform provides neither.
/// <see cref="SettingsEditorTestBase"/> gives a real store over a temporary file, which is what makes
/// <see cref="SettingsStore.IsFirstLaunch"/> genuinely true rather than stubbed.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class FirstLaunchTests : SettingsEditorTestBase
{
    private readonly IGlobalHotkeyService _hotkeys = A.Fake<IGlobalHotkeyService>();

    private readonly RecordingNotificationService _notifications = new();

    [AvaloniaFact]
    public void AFirstLaunchAnnouncesItselfAndOpensTheWindow()
    {
        Store.IsFirstLaunch.ShouldBeTrue("the base creates its store over a file that did not exist");

        var shown = 0;

        App.ShowFirstLaunch(Store, _notifications, () => shown++);

        shown.ShouldBe(1);
        _notifications.Forced.Count.ShouldBe(1);
        _notifications.Forced[0].Title.ShouldBe("Welcome to Pisum Whisper!");
        _notifications.Forced[0].Message.ShouldContain("provider");
        _notifications.Informational.ShouldBeEmpty();
    }

    /// <summary>
    /// The welcome points at the window, so it must not land on top of the thing it is pointing at.
    /// </summary>
    [AvaloniaFact]
    public void TheWelcomeIsRaisedBeforeTheWindowIsShown()
    {
        var order = new List<string>();

        _notifications.OnNotify = () => order.Add("notified");

        App.ShowFirstLaunch(Store, _notifications, () => order.Add("shown"));

        order.ShouldBe(["notified", "shown"]);
    }

    [AvaloniaFact]
    public void ASubsequentLaunchDoesNeither()
    {
        // The first store's Load wrote the file, so a second one over the same path is an ordinary
        // launch — which is exactly what the running application sees on every start but the first.
        var again = new SettingsStore(NullLogger<SettingsStore>.Instance, Store.FilePath);
        again.Load();
        again.IsFirstLaunch.ShouldBeFalse();

        var shown = 0;

        App.ShowFirstLaunch(again, _notifications, () => shown++);

        shown.ShouldBe(0);
        _notifications.Forced.ShouldBeEmpty();
    }

    /// <summary>
    /// Dismissing the window that opened itself leaves the application in the tray rather than
    /// quitting it — change 10's <c>WindowClosing</c> behaviour, unchanged by opening it here.
    /// </summary>
    [AvaloniaFact]
    public void ClosingTheWindowAfterAFirstLaunchHidesItAndLeavesTheApplicationRunning()
    {
        var window = new SettingsWindow(NewViewModel());

        App.ShowFirstLaunch(Store, _notifications, () =>
        {
            window.Show();
            window.Activate();
        });

        window.IsVisible.ShouldBeTrue();

        var cancelled = WindowInternals.Close(window, WindowCloseReason.WindowClosing);

        cancelled.ShouldBeTrue("the close is turned into a hide");
        window.IsVisible.ShouldBeFalse();
    }

    private SettingsWindowViewModel NewViewModel()
    {
        A.CallTo(() => _hotkeys.Availability).Returns(HotkeyAvailability.Available);

        return new SettingsWindowViewModel(
            Store,
            NewEditor(),
            A.Fake<IGeminiKeyProbe>(),
            _hotkeys,
            new LogDirectory(Path.Combine(Path.GetTempPath(), "pisum-whisper-tests-logs")),
            A.Fake<ISystemShell>(),
            NullLoggerFactory.Instance);
    }

    /// <summary>
    /// Keeps the two kinds apart, so "forced" is asserted rather than assumed — the welcome must not
    /// be the one message a silenced user never sees.
    /// </summary>
    private sealed class RecordingNotificationService : INotificationService
    {
        public List<(string Title, string Message)> Forced { get; } = [];

        public List<(string Title, string Message)> Informational { get; } = [];

        public Action? OnNotify { get; set; }

        public void Notify(string title, string message)
        {
            Forced.Add((title, message));
            OnNotify?.Invoke();
        }

        public void NotifyInformation(string title, string message)
        {
            Informational.Add((title, message));
        }
    }
}
