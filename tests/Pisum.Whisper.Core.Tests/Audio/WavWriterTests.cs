namespace Pisum.Whisper.Core.Tests.Audio;

using System.Text;
using Pisum.Whisper.Core.Audio;
using Shouldly;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class WavWriterTests
{
    [Fact]
    public void Write_ProducesA16BitMonoPcmRiffHeader()
    {
        float[] samples = [0f, 0.5f, -0.5f, 1f, -1f];

        var bytes = new WavWriter().Write(samples, 48_000);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        Encoding.ASCII.GetString(reader.ReadBytes(4)).ShouldBe("RIFF");
        reader.ReadInt32().ShouldBe(36 + samples.Length * 2);
        Encoding.ASCII.GetString(reader.ReadBytes(4)).ShouldBe("WAVE");

        Encoding.ASCII.GetString(reader.ReadBytes(4)).ShouldBe("fmt ");
        reader.ReadInt32().ShouldBe(16);
        reader.ReadInt16().ShouldBe((short) 1); // PCM
        reader.ReadInt16().ShouldBe((short) 1); // mono
        reader.ReadInt32().ShouldBe(48_000); // sample rate
        reader.ReadInt32().ShouldBe(48_000 * 2); // byte rate
        reader.ReadInt16().ShouldBe((short) 2); // block align
        reader.ReadInt16().ShouldBe((short) 16); // bits per sample

        Encoding.ASCII.GetString(reader.ReadBytes(4)).ShouldBe("data");
        reader.ReadInt32().ShouldBe(samples.Length * 2);
    }

    [Fact]
    public void Write_ClampsOutOfRangeSamplesRatherThanOverflowing()
    {
        var bytes = new WavWriter().Write([2f, -2f], 8_000);

        // The header is 44 bytes; two 16-bit samples follow it.
        BitConverter.ToInt16(bytes, 44).ShouldBe(short.MaxValue);
        BitConverter.ToInt16(bytes, 46).ShouldBe((short) -short.MaxValue);
    }
}
