namespace Pisum.Whisper.Core.Audio;

/// <summary>
/// An audio capture or encoding operation the system refuses to perform: no input device, or no
/// configured format could be encoded. One type mirrors the reference's single audio error, and its
/// message is written to be shown to the user as-is.
/// </summary>
public sealed class AudioException(string message, Exception? innerException = null)
    : Exception(message, innerException);
