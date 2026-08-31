namespace Pisum.Whisper.Core.Tests.Output;

using System.Diagnostics;
using Pisum.Whisper.Core.Output;
using Shouldly;

/// <summary>
/// Tasks 2.7, 2.8 and 2.10 — putting the user's clipboard back, and the three ways that goes wrong
/// if it is done unguarded, unserialised, or abandoned half way.
/// </summary>
[UnitTest]
public sealed class TextOutputRestoreTests : TextOutputTestBase
{
    private const string Copied = "https://example.invalid/what-the-user-had-copied";

    // ---- Task 2.7: guards 2 and 3 ----

    [Fact]
    public async Task ASuccessfulPaste_PutsThePreviousTextBack()
    {
        Clipboard.Text = Copied;

        await Create().DeliverAsync(Transcript, CancellationToken.None);

        Clipboard.Text.ShouldBe(Copied);
        Clipboard.Writes.ShouldBe([Transcript, Copied]);
    }

    [Fact]
    public async Task AClipboardChangedDuringTheDelivery_IsLeftAlone()
    {
        Clipboard.Text = Copied;
        var output = Create(restoreDelay: TimeSpan.FromMilliseconds(200));

        var delivery = output.DeliverAsync(Transcript, CancellationToken.None);

        // The user copies something while the transcript is still on its way into their document.
        // That copy is newer than anything this delivery saved, so it wins.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Clipboard.Text = "something the user copied mid-dictation";

        await delivery;

        Clipboard.Text.ShouldBe("something the user copied mid-dictation");
        Clipboard.Writes.ShouldBe([Transcript]);
    }

    [Fact]
    public async Task AClipboardThatHeldNoText_IsNotRestored()
    {
        // An empty clipboard, an image and a file list all read as null. Round-tripping arbitrary
        // formats is a non-goal, so the transcript simply stays.
        Clipboard.Text = null;

        await Create().DeliverAsync(Transcript, CancellationToken.None);

        Clipboard.Text.ShouldBe(Transcript);
        Clipboard.Writes.ShouldBe([Transcript]);
    }

    [Fact]
    public async Task ASecondDeliverysTranscript_StandsTheFirstRestoreDown()
    {
        // The gate makes this sequential, so what is asserted is that the second delivery's write
        // is what the first one reads back — and that guard 2 recognises it as not its own.
        Clipboard.Text = Copied;
        var output = Create();

        await output.DeliverAsync(Transcript, CancellationToken.None);
        await output.DeliverAsync("a second dictation", CancellationToken.None);

        Clipboard.Text.ShouldBe(Copied);
    }

    // ---- Task 2.8: cancellation ----

    [Fact]
    public async Task CancellingBetweenThePasteAndTheRestore_StillRestores_AndDoesNotWait()
    {
        Clipboard.Text = Copied;
        using var cancellation = new CancellationTokenSource();
        var output = Create(restoreDelay: TimeSpan.FromSeconds(30));

        var stopwatch = Stopwatch.StartNew();
        var delivery = output.DeliverAsync(Transcript, cancellation.Token);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        (await delivery).ShouldBe(TextOutputOutcome.Pasted);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        Clipboard.Text.ShouldBe(Copied, "the user's clipboard exists nowhere else at that moment");
    }

    [Fact]
    public async Task CancellingBeforeTheWrite_LeavesTheClipboardUntouched()
    {
        Clipboard.Text = Copied;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var output = Create();

        await Should.ThrowAsync<OperationCanceledException>(
            () => output.DeliverAsync(Transcript, cancellation.Token));

        Clipboard.Text.ShouldBe(Copied);
        Clipboard.Writes.ShouldBeEmpty();
    }

    // ---- Task 2.10: deliveries are serialised ----

    [Fact]
    public async Task TwoOverlappingDeliveries_LeaveTheUsersOwnTextOnTheClipboard()
    {
        // Unserialised, every guard behaves exactly as specified and the result is still wrong: the
        // second delivery reads the first one's transcript, takes it for the user's clipboard, and
        // faithfully restores a transcript over contents held nowhere at all.
        Clipboard.Text = Copied;
        var output = Create(restoreDelay: TimeSpan.FromMilliseconds(200));

        var first = output.DeliverAsync(Transcript, CancellationToken.None);
        var second = output.DeliverAsync("a second dictation", CancellationToken.None);

        await Task.WhenAll(first, second);

        Clipboard.Text.ShouldBe(Copied);
        Clipboard.Writes.ShouldBe([Transcript, Copied, "a second dictation", Copied]);
    }

    [Fact]
    public async Task ADeliveryThatThrew_ReleasesTheGate()
    {
        var output = Create();
        Clipboard.WriteFailure = new InvalidOperationException("the clipboard could not be emptied");

        await Should.ThrowAsync<TextOutputException>(() => output.DeliverAsync(Transcript, CancellationToken.None));

        Clipboard.WriteFailure = null;

        // Without the finally this would deadlock rather than fail.
        var outcome = await output.DeliverAsync(Transcript, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        outcome.ShouldBe(TextOutputOutcome.Pasted);
    }
}
