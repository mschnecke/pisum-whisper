namespace Pisum.Whisper.Core.Audio;

using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;

/// <summary>
/// Encodes mono audio to Ogg/Opus via <see cref="OpusOggWriteStream"/>, which owns framing, tail
/// handling and the Ogg container rather than a hand-rolled muxer — see spike S4 in
/// <c>openspec/changes/archive/2026-08-27-bootstrap-solution/design.md</c> and this change's
/// <c>design.md</c> for the constructor parameters and the resulting pre-skip-0 deviation from the
/// reference's 312.
/// </summary>
public sealed class OggOpusWriter
{
    private const int Bitrate = 24_000;

    private const int ResamplerQuality = 5;

    public byte[] Write(float[] samples, int sampleRate)
    {
        var encoder = OpusCodecFactory.CreateEncoder(sampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = Bitrate;

        using var stream = new MemoryStream();
        var ogg = new OpusOggWriteStream(encoder, stream, new OpusTags(), sampleRate);
        ogg.WriteSamples(samples, 0, samples.Length);
        ogg.Finish();

        return stream.ToArray();
    }
}
