namespace Pisum.Whisper.App.Tests.Notifications;

using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
        var presenter = new ToastPresenter(Dwell);

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
        var presenter = new ToastPresenter(Dwell);

        await Task.Run(() => presenter.Present("Authentication Error", "The configured key was rejected."));

        Dispatcher.UIThread.RunJobs();

        presenter.LiveCount.ShouldBe(1);

        presenter.CloseAll();
    }

    [AvaloniaFact]
    public void ThreeStackAndAFourthClosesTheOldest()
    {
        var presenter = new ToastPresenter(Dwell);

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
        var presenter = new ToastPresenter(Dwell);

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
        var presenter = new ToastPresenter(TimeSpan.FromMinutes(5));

        presenter.Present("Network Error", "the provider could not be reached");
        presenter.Present("Rate Limit Error", "the provider is overloaded");
        Dispatcher.UIThread.RunJobs();
        presenter.LiveCount.ShouldBe(2);

        presenter.CloseAll();

        presenter.LiveCount.ShouldBe(0);
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
}
