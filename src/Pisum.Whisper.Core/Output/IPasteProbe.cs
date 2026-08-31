namespace Pisum.Whisper.Core.Output;

/// <summary>
/// Answers whether a synthetic paste keystroke can reach the application that currently holds
/// keyboard focus, implemented natively in <c>Pisum.Whisper.Platform</c> beside the clipboard and
/// named for the <see cref="Transcription.IGeminiKeyProbe"/> this codebase already has.
/// </summary>
/// <remarks>
/// This exists because the alternative failure is undetectable. Both platforms discard injected
/// input silently and report success — Windows for a window of higher integrity than this process,
/// macOS without an Accessibility grant — so a delivery that does not ask first waits, finds its own
/// transcript still on the clipboard, restores the previous contents over it, and the user's speech
/// is gone with no message at all.
/// </remarks>
public interface IPasteProbe
{
    /// <summary>
    /// Whether the paste is worth attempting. A false negative costs the user a manual Ctrl+V with
    /// their transcript intact; a false positive is the silent loss above, so implementations answer
    /// <see langword="true"/> whenever they cannot tell.
    /// </summary>
    bool CanPaste();
}
