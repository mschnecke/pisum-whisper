namespace Pisum.Whisper.Core.Diagnostics;

/// <summary>
/// Shows the user a failure that stopped the application starting.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not <see cref="Pisum.Whisper.Core.Notifications.INotificationService"/>, and the split
/// is by when a failure happens rather than by how bad it is.</b> Everything a notification is drawn
/// as needs a dispatcher pumping a queue; every implementation of this one runs before there is one,
/// or after Avalonia has failed to give us one at all.
/// </para>
/// <para>
/// <b>It is constructed, never registered.</b> One of its call sites is the service container
/// failing to build, so a reporter resolved from the container is a reporter that does not exist
/// exactly when it is needed. <c>NativeFatalErrorReporter.Create()</c> in
/// <c>Pisum.Whisper.Platform</c> is how <c>Program</c> obtains one, on its first line.
/// </para>
/// <para>
/// <b><see cref="Report"/> never throws.</b> It runs while a failure is already being handled;
/// losing the dialog is bad, and losing the exit code and the log line behind it is worse.
/// </para>
/// </remarks>
public interface IFatalErrorReporter
{
    /// <summary>Shows <paramref name="title"/> and <paramref name="message"/>, and returns once the user has dismissed them.</summary>
    void Report(string title, string message);
}
