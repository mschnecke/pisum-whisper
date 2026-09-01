namespace Pisum.Whisper.Core.Notifications;

/// <summary>
/// Puts a notification on the screen. The transport, with no policy of its own.
/// </summary>
/// <remarks>
/// <para>
/// The seam sits here rather than at <see cref="INotificationService"/> so that the
/// forced-versus-suppressible policy stays in <c>Core</c> while the presentation stays where the
/// tray icon and the settings window already are — <c>Pisum.Whisper.App</c>, because it is Avalonia.
/// It is also the seam a native transport would replace if change 12's packaging ever makes one
/// worth having.
/// </para>
/// <para>
/// <b><see cref="Present"/> must not block, and that is a correctness rule rather than a
/// preference.</b> It is called from the hotkey's dispatch loop, where the very next item may be the
/// release edge that ends a hold-to-record dictation, so an implementation that waits for a window
/// to appear is wrong. This is the same constraint that keeps <c>TextOutput</c> out of a hook
/// handler, applied one layer further out, and it is what disqualifies a transport that shells out
/// per notification.
/// </para>
/// </remarks>
public interface INotificationPresenter
{
    /// <summary>Shows <paramref name="title"/> and <paramref name="message"/>, returning immediately.</summary>
    void Present(string title, string message);
}
