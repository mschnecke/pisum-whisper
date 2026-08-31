namespace Pisum.Whisper.Core.Tests.Audio;

using Concentus;
using Concentus.Oggfile;
using Pisum.Whisper.Core.Audio;
using Shouldly;

[UnitTest]
public sealed class OggOpusWriterTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void Write_ProducesAStreamThatDecodesBackToTheSourceDuration()
    {
        var samples = GenerateTone(seconds: 1);

        var bytes = new OggOpusWriter().Write(samples, SampleRate);

        var decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1);
        using var stream = new MemoryStream(bytes);
        var reader = new OpusOggReadStream(decoder, stream);

        var decodedSamples = 0;
        while (reader.HasNextPacket)
        {
            var pcm = reader.DecodeNextPacket();
            if (pcm is null)
            {
                break;
            }

            decodedSamples += pcm.Length;
        }

        // Same 97-103% tolerance spike S4 used, since the tail frame is zero-padded to a full frame.
        var ratio = decodedSamples / (double)samples.Length;
        ratio.ShouldBeInRange(0.97, 1.03);
    }

    private static float[] GenerateTone(int seconds)
    {
        var samples = new float[SampleRate * seconds];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Sin(2 * Math.PI * 440 * i / SampleRate);
        }

        return samples;
    }
}
