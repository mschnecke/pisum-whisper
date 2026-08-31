namespace Pisum.Whisper.Core.Output;

/// <summary>
/// Delivers a transcript to the cursor of whichever application holds keyboard focus.
/// </summary>
/// <remarks>
/// One member, because the steps behind it — read the previous clipboard, write the transcript,
/// paste, restore — are invariants about each other rather than a sequence a caller can order.
/// "Never restore after a failed paste" and "only restore what is still ours" are correctness rules
/// about the pair, and a caller free to sequence two thinner services is a caller free to violate
/// them.
/// </remarks>
public interface ITextOutput
{
    /// <summary>
    /// Places <paramref name="transcript"/> on the clipboard, sends the platform's paste keystroke,
    /// and puts the clipboard's previous text back afterwards.
    /// </summary>
    /// <param name="transcript">
    /// The text to deliver. Leading and trailing whitespace is removed before anything else happens.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the delivery. Once the transcript has been written the restore is owed, so from that
    /// point cancellation shortens the wait before it rather than abandoning it.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="transcript"/> has nothing left once surrounding whitespace is removed.
    /// </exception>
    /// <exception cref="TextOutputException">The transcript could not be placed on the clipboard.</exception>
    Task<TextOutputOutcome> DeliverAsync(string transcript, CancellationToken cancellationToken);
}
