namespace Pisum.Whisper.Core.Output;

/// <summary>
/// A delivery the system could not perform: the transcript could not be placed on the clipboard at
/// all, which is the only outcome in which it is genuinely lost. One type mirrors
/// <see cref="Audio.AudioException"/> and <see cref="Settings.SettingsException"/>, and its message
/// is written to be shown to the user as-is.
/// </summary>
/// <remarks>
/// A paste that fails is not this: the transcript is on the clipboard, so it is reported as
/// <see cref="TextOutputOutcome.ClipboardOnly"/> rather than thrown.
/// </remarks>
public sealed class TextOutputException(string message, Exception? innerException = null)
    : Exception(message, innerException);
