namespace Pisum.Whisper.App.Notifications;

using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Notifications;

/// <summary>
/// Shows notifications as <see cref="ToastWindow"/>s, stacks them, and takes them away again.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Present"/> posts and returns.</b> It may not wait for the window, and an
/// implementation that does is wrong: two of the call sites run on the hotkey's dispatch loop, where
/// the very next item may be the release edge that ends a hold-to-record dictation. That is the same
/// constraint that keeps <c>TextOutput</c> out of a hook handler, and it is what disqualifies a
/// transport that shells out per notification.
/// </para>
/// <para>
/// Everything below the post runs on the UI thread and only there, which is what lets
/// <see cref="_live"/> be an ordinary list with no lock.
/// </para>
/// <para>
/// <b><see cref="Present"/> is gated on the UI thread already owning a dispatcher.</b> A notification
/// raised between <c>host.Start()</c> and Avalonia's initialisation runs on a pooled thread; touching
/// <see cref="Dispatcher.UIThread"/> there would create the process's one dispatcher on that pooled
/// thread instead of the main one, and Avalonia's own initialisation then fails with a
/// thread-ownership exception it cannot recover from. Asking <see cref="Dispatcher.FromThread"/>
/// whether the captured UI thread has a dispatcher yet, instead of touching <c>UIThread</c> directly,
/// is what avoids creating one from the wrong thread.
/// </para>
/// </remarks>
public sealed class ToastPresenter : INotificationPresenter
{
    /// <summary>
    /// How long a notification stays up. The user is working in another application and may not be
    /// looking at the screen, so it goes away on its own and never waits to be clicked.
    /// </summary>
    private static readonly TimeSpan DefaultDwell = TimeSpan.FromSeconds(6);

    /// <summary>
    /// At most this many at once. Failures can arrive together — a transcription failure followed by
    /// a delivery failure — and unbounded stacking marches off the screen.
    /// </summary>
    private const int MaxConcurrent = 3;

    private readonly TimeSpan _dwell;

    private readonly ILogger<ToastPresenter> _logger;

    /// <summary>Captured at construction, on the main thread, before Avalonia is initialised.</summary>
    private readonly Thread _uiThread;

    /// <summary>Oldest first, which is both the stacking order and which one a fourth displaces.</summary>
    private readonly List<Live> _live = [];

    public ToastPresenter(ILogger<ToastPresenter> logger)
        : this(DefaultDwell, logger, Thread.CurrentThread)
    {
    }

    /// <summary>
    /// Constructs the presenter over an explicit dwell, so no test waits six seconds — the same
    /// shape as <c>SettingsEditor</c>'s injected debounce and <c>GeminiProvider</c>'s injected
    /// backoff.
    /// </summary>
    internal ToastPresenter(TimeSpan dwell, ILogger<ToastPresenter> logger)
        : this(dwell, logger, Thread.CurrentThread)
    {
    }

    /// <summary>Constructs the presenter naming the UI thread explicitly, so a test can name one that owns no dispatcher.</summary>
    internal ToastPresenter(TimeSpan dwell, ILogger<ToastPresenter> logger, Thread uiThread)
    {
        _dwell = dwell;
        _logger = logger;
        _uiThread = uiThread;
    }

    /// <summary>How many notifications are on screen. The UI thread's view of it.</summary>
    internal int LiveCount => _live.Count;

    public void Present(string title, string message)
    {
        var dispatcher = Dispatcher.FromThread(_uiThread);

        if (dispatcher is null)
        {
            _logger.LogWarning(
                "A notification was raised before the UI was ready and was dropped: {Title}", title);

            return;
        }

        dispatcher.Post(() => Show(title, message));
    }

    /// <summary>
    /// Takes every notification away. Called from <c>App.OnExit</c>, so that an open one cannot
    /// outlive the dispatcher that owns it.
    /// </summary>
    public void CloseAll()
    {
        foreach (var live in _live.ToArray())
        {
            Dismiss(live);
        }
    }

    private void Show(string title, string message)
    {
        if (_live.Count >= MaxConcurrent)
        {
            // The most recent are the ones worth reading, so the oldest goes rather than the newest
            // being refused.
            Dismiss(_live[0]);
        }

        var window = new ToastWindow(title, message);
        var timer = new DispatcherTimer {Interval = _dwell};
        var live = new Live(window, timer);

        timer.Tick += (_, _) => Dismiss(live);

        _live.Add(live);
        Restack();

        window.Show();
        timer.Start();
    }

    private void Dismiss(Live live)
    {
        if (!_live.Remove(live))
        {
            // The dwell elapsed at the same moment a fourth notification displaced this one, or the
            // application is exiting. Either way it is already gone.
            return;
        }

        live.Timer.Stop();
        live.Window.Close();

        Restack();
    }

    /// <summary>
    /// Re-places every live notification, because a slot freed in the middle would otherwise leave a
    /// gap and the next one would be placed on top of an existing one.
    /// </summary>
    private void Restack()
    {
        for (var slot = 0; slot < _live.Count; slot++)
        {
            _live[slot].Window.PlaceInCorner(slot);
        }
    }

    private sealed record Live(ToastWindow Window, DispatcherTimer Timer);
}
