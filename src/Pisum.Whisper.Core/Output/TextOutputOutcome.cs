namespace Pisum.Whisper.Core.Output;

/// <summary>
/// How far a delivery got. Both values are successes in the sense that the transcript survived; they
/// differ in whether the user still has to press the paste combination themselves.
/// </summary>
public enum TextOutputOutcome
{
    /// <summary>The paste keystroke was sent and the previous clipboard contents were dealt with.</summary>
    Pasted,

    /// <summary>
    /// The transcript is on the clipboard and the paste was not sent, or was sent and refused.
    /// Nothing was restored over it, so a manual paste still produces the transcript.
    /// </summary>
    ClipboardOnly,
}
