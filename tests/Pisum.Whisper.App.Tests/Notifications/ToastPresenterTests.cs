namespace Pisum.Whisper.App.Tests.Notifications;

using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.App.Notifications;
using Shouldly;

/// <summary>
/// Task 2.2 — the presenter: it posts rather than waits, stacks at most three, and takes each one
/// away on its own.
/// </summary>
/// <remarks>
/// The dwell is injected at a few milliseconds, so nothing here waits the six seconds the running
/// application uses.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class ToastPresenterTests
{
    private static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// The rule the whole transport is chosen for: two call sites run on the hotkey's dispatch loop,
    /// so <c>Present</c> may not wait for a window. Called from the UI thread, the posted job cannot
    /// have run yet — which is the strongest form of "it returned first" a single-threaded
    /// dispatcher can express.
    /// </summary>
    [AvaloniaFact]
    public void PresentReturnsBeforeTheWindowExists()
    {
        var presenter = new ToastPresenter(Dwell, NullLogger<ToastPresenter>.Instance);

        presenter.Present("Recording Error", "No input device found.");

        presenter.LiveCount.ShouldBe(0);

        Dispatcher.UIThread.RunJobs();

        presenter.LiveCount.ShouldBe(1);

        presenter.CloseAll();
    }

    /// <summary>
    /// Both of the call sites on the hotkey's dispatch loop are pooled threads, never the UI thread.
    /// </summary>
    [AvaloniaFact]
    public async Task PresentCompletesWhenCalledFromANonUiThread()
    {
        var presenter = new ToastPresenter(Dwell, NullLogger<ToastPresenter>.Instance);

        await Task.Run(() => presenter.Present("Authentication Error", "The configured key was rejected."));

        Dispatcher.UIThread.RunJobs();

        presenter.LiveCount.ShouldBe(1);

        presenter.CloseAll();
    }

    [AvaloniaFact]
    public void ThreeStackAndAFourthClosesTheOldest()
    {
        var presenter = new ToastPresenter(Dwell, NullLogger<ToastPresenter>.Instance);

        for (var index = 0; index < 4; index++)
        {
            presenter.Present($"Error {index}", "something went wrong");
        }

        Dispatcher.UIThread.RunJobs();

        presenter.LiveCount.ShouldBe(3);

        presenter.CloseAll();
        presenter.LiveCount.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task ANotificationGoesAwayOnItsOwn()
    {
        var presenter = new ToastPresenter(Dwell, NullLogger<ToastPresenter>.Instance);

        presenter.Present("Paste Failed", "The text was copied to the clipboard.");
        Dispatcher.UIThread.RunJobs();
        presenter.LiveCount.ShouldBe(1);

        (await WaitForAsync(() => presenter.LiveCount == 0))
            .ShouldBeTrue("the notification should have dismissed itself after the dwell");
    }

    /// <summary>Quitting with one on screen takes it away, so none outlives the dispatcher that owns it.</summary>
    [AvaloniaFact]
    public void CloseAllRemovesEveryLiveNotification()
    {
        var presenter = new ToastPresenter(TimeSpan.FromMinutes(5), NullLogger<ToastPresenter>.Instance);

        presenter.Present("Network Error", "the provider could not be reached");
        presenter.Present("Rate Limit Error", "the provider is overloaded");
        Dispatcher.UIThread.RunJobs();
        presenter.LiveCount.ShouldBe(2);

        presenter.CloseAll();

        presenter.LiveCount.ShouldBe(0);
    }

    /// <summary>
    /// Task 4.3 (settle-win-x64-verification-debt) — the readiness gate: a notification raised for a
    /// UI thread that owns no dispatcher yet is dropped rather than creating one on the calling
    /// thread, which is what a pooled-thread <c>Present</c> before Avalonia's initialisation would
    /// otherwise do.
    /// </summary>
    [Fact]
    public void PresentBeforeTheUiThreadHasADispatcherIsDroppedAndLogged()
    {
        var logger = new CapturingLogger();
        var uninitialisedThread = new Thread(() => { });
        var presenter = new ToastPresenter(Dwell, logger, uninitialisedThread);

        presenter.Present("Startup Error", "Pisum Whisper could not start.");

        presenter.LiveCount.ShouldBe(0);
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("Startup Error"));
    }

    /// <summary>The existing behaviour restated against the gate: once the UI thread has a dispatcher, showing is unaffected.</summary>
    [AvaloniaFact]
    public void PresentAfterTheUiThreadHasADispatcherShows()
    {
        var presenter = new ToastPresenter(Dwell, NullLogger<ToastPresenter>.Instance);

        presenter.Present("Recording Error", "No input device found.");
        Dispatcher.UIThread.RunJobs();

        presenter.LiveCount.ShouldBe(1);

        presenter.CloseAll();
    }

    /// <summary>Waits on the dispatcher rather than on a thread, because the timer runs on it.</summary>
    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();

        while (!condition() && deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }

        return condition();
    }

    private sealed class CapturingLogger : ILogger<ToastPresenter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
