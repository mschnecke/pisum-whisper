namespace Pisum.Whisper.Core.Tests.Notifications;

using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Notifications;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>
/// Task 1.1 — the forced-versus-suppressible policy, over a real settings store.
/// </summary>
/// <remarks>
/// A real store rather than a fake because the flag being read is the one a user actually toggles,
/// and holding a non-default value means writing a file. That is what makes this
/// <c>Integration</c> rather than <c>Unit</c>.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class NotificationServiceTests : IDisposable
{
    private const string ErrorTitle = "Authentication Error";

    private const string ErrorMessage = "The configured key was rejected.";

    private const string StatusTitle = "Transcription In Progress";

    private const string StatusMessage = "Please wait for the current transcription to finish.";

    private readonly string _home;

    private readonly SettingsStore _settings;

    private readonly FakeNotificationPresenter _presenter = new();

    private readonly INotificationService _notifications;

    public NotificationServiceTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);

        _settings = new SettingsStore(
            NullLogger<SettingsStore>.Instance,
            Path.Combine(_home, ".pisum-whisper.json"));

        _settings.Load();

        _notifications = new NotificationService(_settings, _presenter);
    }

    public void Dispose()
    {
        Directory.Delete(_home, true);
    }

    [Fact]
    public void AFailureIsShownEvenWithTheNotificationsPreferenceOff()
    {
        ShowTrayNotifications(false);

        _notifications.Notify(ErrorTitle, ErrorMessage);

        _presenter.Presented.ShouldBe([(ErrorTitle, ErrorMessage)]);
    }

    [Fact]
    public void AStatusMessageIsSuppressedWithThePreferenceOff()
    {
        ShowTrayNotifications(false);

        _notifications.NotifyInformation(StatusTitle, StatusMessage);

        _presenter.Count.ShouldBe(0);
    }

    [Fact]
    public void BothAreShownWithThePreferenceOn()
    {
        ShowTrayNotifications(true);

        _notifications.Notify(ErrorTitle, ErrorMessage);
        _notifications.NotifyInformation(StatusTitle, StatusMessage);

        _presenter.Presented.ShouldBe([(ErrorTitle, ErrorMessage), (StatusTitle, StatusMessage)]);
    }

    /// <summary>
    /// The preference is read per call, so changing it takes effect on the very next notification —
    /// with nothing re-registered, rebuilt or subscribed to.
    /// </summary>
    [Fact]
    public void ThePreferenceIsReadPerCall()
    {
        ShowTrayNotifications(true);
        _notifications.NotifyInformation(StatusTitle, StatusMessage);

        ShowTrayNotifications(false);
        _notifications.NotifyInformation(StatusTitle, StatusMessage);

        _presenter.Count.ShouldBe(1);

        ShowTrayNotifications(true);
        _notifications.NotifyInformation(StatusTitle, StatusMessage);

        _presenter.Count.ShouldBe(2);
    }

    private void ShowTrayNotifications(bool enabled)
    {
        var settings = _settings.CloneCurrent();
        settings.ShowTrayNotifications = enabled;
        _settings.Save(settings);
    }
}
