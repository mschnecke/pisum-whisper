namespace Pisum.Whisper.Core.Audio;

using Pisum.Whisper.Core.Settings;

/// <summary>
/// Encodes captured samples to the format configured in settings, falling back to the other format
/// if the preferred one fails.
/// </summary>
public interface IAudioEncoder
{
    EncodedAudio Encode(float[] samples, int sampleRate, AudioFormat preferred);
}
