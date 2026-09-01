namespace Pisum.Whisper.Core.Notifications;

using Pisum.Whisper.Core.Settings;

/// <summary>
/// The whole policy: failures are forced, status messages respect
/// <see cref="AppSettings.ShowTrayNotifications"/>.
/// </summary>
/// <remarks>
/// The preference is read from <see cref="SettingsStore.Current"/> <b>per call</b>, which is what
/// makes a change to it take effect without a restart and without anything being rebuilt. There is
/// no subscription to <see cref="SettingsStore.Changed"/> and no state, matching
/// <c>GeminiProviderPool</c> and <c>DictationOrchestrator</c>.
/// </remarks>
internal sealed class NotificationService : INotificationService
{
    private readonly SettingsStore _settings;

    private readonly INotificationPresenter _presenter;

    public NotificationService(SettingsStore settings, INotificationPresenter presenter)
    {
        _settings = settings;
        _presenter = presenter;
    }

    public void Notify(string title, string message)
    {
        _presenter.Present(title, message);
    }

    public void NotifyInformation(string title, string message)
    {
        if (!_settings.Current.ShowTrayNotifications)
        {
            return;
        }

        _presenter.Present(title, message);
    }
}
