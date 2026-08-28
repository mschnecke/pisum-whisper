namespace Pisum.Whisper.Core.Audio;

/// <summary>
/// Writes 16-bit PCM mono audio as a WAV (RIFF) file, matching the reference's <c>hound::WavSpec</c>
/// shape. No NuGet package is pinned for WAV, and the format is small enough that hand-writing the
/// header is simpler than adding a dependency for it.
/// </summary>
public sealed class WavWriter
{
    private const short BitsPerSample = 16;
    private const short Channels = 1;
    private const short BytesPerSample = BitsPerSample / 8;

    public byte[] Write(float[] samples, int sampleRate)
    {
        var dataSize = samples.Length * BytesPerSample;
        var byteRate = sampleRate * Channels * BytesPerSample;
        var blockAlign = (short)(Channels * BytesPerSample);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16); // fmt chunk size for PCM
        writer.Write((short)1); // PCM
        writer.Write(Channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(BitsPerSample);

        writer.Write("data"u8);
        writer.Write(dataSize);
        foreach (var sample in samples)
        {
            var clamped = Math.Clamp(sample, -1f, 1f);
            writer.Write((short)(clamped * short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }
}
