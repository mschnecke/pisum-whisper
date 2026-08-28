namespace Pisum.Whisper.Core.Audio;

using Pisum.Whisper.Core.Settings;

/// <summary>
/// The result of encoding a recording: the bytes, the MIME type they should be uploaded with, and
/// which format was actually used — a fallback encode can silently swap this from the caller's
/// preferred format, matching the reference's <c>opus_mime_type()</c>/<c>wav_mime_type()</c> pairing.
/// </summary>
public readonly record struct EncodedAudio(byte[] Bytes, string MimeType, AudioFormat ActualFormat)
{
    public const string OpusMimeType = "audio/ogg";
    public const string WavMimeType = "audio/wav";
}
