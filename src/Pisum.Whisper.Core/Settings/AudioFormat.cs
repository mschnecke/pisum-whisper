namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// Defines audio file formats supported for encoding and decoding operations.
/// </summary>
public enum AudioFormat
{
    /// <summary>
    /// Represents the Opus audio format, a highly efficient,
    /// lossy compression codec designed for interactive speech and music streaming.
    /// </summary>
    Opus,

    /// <summary>
    /// Represents the WAV audio format, an uncompressed, lossless format
    /// commonly used for high-quality audio recording and playback.
    /// </summary>
    Wav,
}
