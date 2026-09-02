namespace Pisum.Whisper.App.Tests;

using Pisum.Whisper.Core.Notifications;

/// <summary>
/// Keeps the two kinds of notification apart, so "forced" is asserted rather than assumed.
/// </summary>
/// <remarks>
/// Extracted from <see cref="FirstLaunchTests"/>, where it began as a private nested class, because
/// <see cref="StartupConditionsTests"/> makes the same distinction for the same reason: neither the
/// welcome nor a degraded start may be the message a user who silenced status chatter never sees.
/// </remarks>
public sealed class RecordingNotificationService : INotificationService
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
