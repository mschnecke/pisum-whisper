namespace Pisum.Whisper.Core.Transcription;

/// <summary>
/// A transcription the system could not produce. One type for the whole capability, mirroring
/// <see cref="Settings.SettingsException"/> and <see cref="Audio.AudioException"/>, with its message
/// written to be shown to the user as-is — plus a <see cref="Category"/>, because unlike those two
/// this capability fails in five distinguishable ways that a caller needs to tell apart.
/// </summary>
public sealed class TranscriptionException(
    string message,
    ErrorCategory category,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>Why this failed, decided here rather than re-derived from the message by the caller.</summary>
    public ErrorCategory Category { get; } = category;
}
