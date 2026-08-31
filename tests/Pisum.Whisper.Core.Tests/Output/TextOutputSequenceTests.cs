namespace Pisum.Whisper.Core.Tests.Output;

using Pisum.Whisper.Core.Output;
using SharpHook.Data;
using Shouldly;

/// <summary>
/// Tasks 2.1, 2.3, 2.4, 2.5 and 2.6 — the steps of the delivery and the two ways it stops early
/// without losing the transcript.
/// </summary>
[TestClass]
public sealed class TextOutputSequenceTests : TextOutputTestBase
{
    // ---- Task 2.1: read, write, wait, paste ----

    [TestMethod]
    public async Task ADelivery_PutsTheTranscriptOnTheClipboardAndPastesIt()
    {
        var outcome = await Create().DeliverAsync(Transcript, CancellationToken.None);

        outcome.ShouldBe(TextOutputOutcome.Pasted);
        Clipboard.Writes[0].ShouldBe(Transcript);
        Posted.Length.ShouldBe(4);
    }

    [TestMethod]
    public async Task AFailedPaste_ReportsTheTranscriptAsCopiedOnly()
    {
        Provider.PostEventResult = UioHookResult.ErrorSetWindowsHookEx;

        var outcome = await Create().DeliverAsync(Transcript, CancellationToken.None);

        outcome.ShouldBe(TextOutputOutcome.ClipboardOnly);
    }

    [TestMethod]
    public async Task AClipboardThatCannotBeRead_StillDelivers()
    {
        Clipboard.ReadFailure = new InvalidOperationException("the clipboard is held by another application");

        var outcome = await Create().DeliverAsync(Transcript, CancellationToken.None);

        outcome.ShouldBe(TextOutputOutcome.Pasted);
        Clipboard.Writes.ShouldBe([Transcript]);
    }

    // ---- Task 2.3: the trim ----

    [TestMethod]
    public async Task ATranscriptEndingInANewline_ReachesTheClipboardWithoutIt()
    {
        await Create().DeliverAsync("hello world\n", CancellationToken.None);

        Clipboard.Writes[0].ShouldBe("hello world");
    }

    [TestMethod]
    public async Task LineBreaksBetweenWords_Survive()
    {
        await Create().DeliverAsync("  first line\nsecond line\t", CancellationToken.None);

        Clipboard.Writes[0].ShouldBe("first line\nsecond line");
    }

    [TestMethod]
    public async Task WhitespaceOnlyText_IsRejectedWithoutTouchingAnything()
    {
        var output = Create();

        await Should.ThrowAsync<ArgumentException>(() => output.DeliverAsync(" \r\n\t ", CancellationToken.None));

        Clipboard.Reads.ShouldBe(0);
        Clipboard.Writes.ShouldBeEmpty();
        Posted.ShouldBeEmpty();
    }

    // ---- Task 2.4: the probe ----

    [TestMethod]
    public async Task ARefusingProbe_LeavesTheTranscriptOnTheClipboardAndSendsNothing()
    {
        Clipboard.Text = "what the user had copied";
        Probe.Allow = false;

        var outcome = await Create().DeliverAsync(Transcript, CancellationToken.None);

        outcome.ShouldBe(TextOutputOutcome.ClipboardOnly);
        Posted.ShouldBeEmpty();
        Clipboard.Text.ShouldBe(Transcript, "restoring over it would make the manual-paste advice a lie");
    }

    [TestMethod]
    public async Task AnAcceptingProbe_SendsTheKeystroke()
    {
        await Create().DeliverAsync(Transcript, CancellationToken.None);

        Probe.Calls.ShouldBe(1);
        Posted.Length.ShouldBe(4);
    }

    // ---- Task 2.5: a clipboard that cannot be written ----

    [TestMethod]
    public async Task AClipboardThatCannotBeWritten_FailsTheDeliveryAndSendsNoKeystroke()
    {
        Clipboard.WriteFailure = new InvalidOperationException("the clipboard could not be emptied");
        var output = Create();

        var exception = await Should.ThrowAsync<TextOutputException>(
            () => output.DeliverAsync(Transcript, CancellationToken.None));

        exception.Message.ShouldNotBeNullOrWhiteSpace();
        Posted.ShouldBeEmpty();
    }

    // ---- Task 2.6: guard 1 ----

    [TestMethod]
    public async Task AFailedPaste_RestoresNothingOverTheTranscript()
    {
        Clipboard.Text = "what the user had copied";
        Provider.PostEventResult = UioHookResult.ErrorSetWindowsHookEx;

        await Create().DeliverAsync(Transcript, CancellationToken.None);

        Clipboard.Text.ShouldBe(Transcript);
        Clipboard.Writes.ShouldBe([Transcript], "a second write would be the restore that must not happen");
    }
}
