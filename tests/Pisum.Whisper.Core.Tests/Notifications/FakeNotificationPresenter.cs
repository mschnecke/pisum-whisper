namespace Pisum.Whisper.Core.Tests.Notifications;

using Pisum.Whisper.Core.Notifications;

/// <summary>Records every notification it was asked to show, in order.</summary>
public sealed class FakeNotificationPresenter : INotificationPresenter
{
    private readonly Lock _gate = new();

    private readonly List<(string Title, string Message)> _presented = [];

    public IReadOnlyList<(string Title, string Message)> Presented
    {
        get
        {
            lock (_gate)
            {
                return [.. _presented];
            }
        }
    }

    public int Count => Presented.Count;

    /// <summary>When set, presenting throws — a transport whose window will not open.</summary>
    public Exception? Failure { get; set; }

    public void Present(string title, string message)
    {
        lock (_gate)
        {
            _presented.Add((title, message));
        }

        if (Failure is { } failure)
        {
            throw failure;
        }
    }
}
