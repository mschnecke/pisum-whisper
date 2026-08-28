namespace Pisum.Whisper.Core.Audio;

using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// Encodes to the preferred format, falling back to the other format if the preferred one throws —
/// mirroring the reference's <c>transcribe_cloud</c> (<c>hotkey/manager.rs:417-446</c>) exactly.
/// </summary>
public sealed class AudioEncoder : IAudioEncoder
{
    private readonly ILogger<AudioEncoder> _logger;
    private readonly Func<float[], int, byte[]> _writeOpus;
    private readonly Func<float[], int, byte[]> _writeWav;

    public AudioEncoder(ILogger<AudioEncoder> logger)
        : this(logger, new OggOpusWriter().Write, new WavWriter().Write)
    {
    }

    /// <summary>Takes the writers as delegates so tests can substitute a throwing one without a
    /// speculative interface over two single-method, single-implementation writers.</summary>
    internal AudioEncoder(
        ILogger<AudioEncoder> logger,
        Func<float[], int, byte[]> writeOpus,
        Func<float[], int, byte[]> writeWav)
    {
        _logger = logger;
        _writeOpus = writeOpus;
        _writeWav = writeWav;
    }

    public EncodedAudio Encode(float[] samples, int sampleRate, AudioFormat preferred)
    {
        var fallback = preferred == AudioFormat.Opus ? AudioFormat.Wav : AudioFormat.Opus;

        // Catches broadly, unlike this codebase's usual narrow catches: the reference falls back on
        // any encoder error (a `Result::Err` of any variant), and the underlying Opus/WAV writers can
        // fail in ways that are not enumerable up front.
        try
        {
            return EncodeAs(samples, sampleRate, preferred);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Encoding audio as {PreferredFormat} failed; falling back to {FallbackFormat}.",
                preferred,
                fallback);
        }

        try
        {
            return EncodeAs(samples, sampleRate, fallback);
        }
        catch (Exception ex)
        {
            throw new AudioException($"Failed to encode audio as {preferred} or {fallback}.", ex);
        }
    }

    private EncodedAudio EncodeAs(float[] samples, int sampleRate, AudioFormat format) => format switch
    {
        AudioFormat.Opus => new EncodedAudio(
            _writeOpus(samples, sampleRate), EncodedAudio.OpusMimeType, AudioFormat.Opus),
        AudioFormat.Wav => new EncodedAudio(
            _writeWav(samples, sampleRate), EncodedAudio.WavMimeType, AudioFormat.Wav),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
