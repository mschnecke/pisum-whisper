namespace Pisum.Whisper.Core.Notifications;

/// <summary>
/// Tells the user something, without the log file being opened.
/// </summary>
/// <remarks>
/// <para>
/// Two named methods rather than one with a <c>force</c> flag. The split is not stylistic: someone
/// who silences status chatter still has to be told their API key is rejected, and a boolean at six
/// call sites reads worse than two names — which is the shape the reference arrived at as well
/// (<c>tray.rs:76-105</c>).
/// </para>
/// <para>
/// <b>Nothing here may block.</b> Two of the call sites run on the hotkey's dispatch loop, where the
/// next item may be the release edge that ends a recording, so both methods return without waiting
/// for anything to appear on screen. The rule is enforced by
/// <see cref="INotificationPresenter.Present"/>.
/// </para>
/// <para>
/// <b>Nothing here may carry a transcript, an API key, or the user's clipboard contents.</b> A
/// notification is drawn over whatever the user is presenting, so it is a wider disclosure than the
/// log file the same rule already protects.
/// </para>
/// </remarks>
public interface INotificationService
{
    /// <summary>
    /// Shows a failure, whether or not the user has enabled tray notifications.
    /// </summary>
    void Notify(string title, string message);

    /// <summary>
    /// Shows a status message, but only when the user has enabled tray notifications.
    /// </summary>
    void NotifyInformation(string title, string message);
}
