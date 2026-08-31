namespace Pisum.Whisper.Core.Output;

/// <summary>
/// The system clipboard's plain-text contents, implemented natively in
/// <c>Pisum.Whisper.Platform</c>.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia cannot supply a clipboard to this process: in 12.1 the only public route to an
/// <c>IClipboard</c> is <c>TopLevel.Clipboard</c>, and a tray-only application creates no
/// <c>TopLevel</c> at all. The interface lives here because <c>Platform</c> references <c>Core</c>
/// one way, and because everything with a decision in it — the guards, the timing, the trim — is on
/// this side of the seam.
/// </para>
/// <para>
/// Synchronous, because both platform APIs are. Nothing here waits except Windows'
/// <c>OpenClipboard</c> retry, which is bounded at about a tenth of a second.
/// </para>
/// </remarks>
public interface ISystemClipboard
{
    /// <summary>
    /// The clipboard's text, or <see langword="null"/> when it holds nothing or holds something that
    /// is not text — an image or a file list, neither of which this application round-trips.
    /// </summary>
    /// <exception cref="Exception">
    /// The clipboard could not be read at all. Callers treat a read as best effort.
    /// </exception>
    string? TryGetText();

    /// <summary>
    /// Replaces the clipboard's contents with <paramref name="text"/>, carrying every mark its
    /// platform offers for keeping the entry out of clipboard history, cloud synchronisation and
    /// clipboard managers.
    /// </summary>
    /// <remarks>
    /// Best effort, and how far it reaches is the platform's to decide rather than this contract's.
    /// Windows has documented formats covering both Win+V and the cloud clipboard tied to the user's
    /// Microsoft account. macOS exposes no way to opt a pasteboard entry out of Universal Clipboard,
    /// so there the mark is the <c>org.nspasteboard.ConcealedType</c> convention and reaches
    /// clipboard managers only — a transcript still syncs to the user's other Apple devices when
    /// Handoff is on.
    /// </remarks>
    /// <exception cref="Exception">The text could not be placed on the clipboard.</exception>
    void SetText(string text);
}
