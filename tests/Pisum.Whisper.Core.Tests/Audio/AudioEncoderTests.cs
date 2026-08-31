namespace Pisum.Whisper.Core.Tests.Audio;

using FakeItEasy;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Settings;
using Shouldly;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AudioEncoderTests
{
    private static readonly float[] Samples = [0f, 0.1f, -0.1f];

    [Fact]
    public void Encode_WhenThePreferredWriterSucceeds_ReturnsItUntouched()
    {
        byte[] opusBytes = [1, 2, 3];
        var encoder = new AudioEncoder(
            A.Fake<ILogger<AudioEncoder>>(),
            (_, _) => opusBytes,
            (_, _) => throw new InvalidOperationException("wav should not be called"));

        var result = encoder.Encode(Samples, 48_000, AudioFormat.Opus);

        result.Bytes.ShouldBe(opusBytes);
        result.MimeType.ShouldBe(EncodedAudio.OpusMimeType);
        result.ActualFormat.ShouldBe(AudioFormat.Opus);
    }

    [Fact]
    public void Encode_WhenThePreferredWriterThrows_FallsBackToTheOtherFormat()
    {
        byte[] wavBytes = [4, 5, 6];
        var encoder = new AudioEncoder(
            A.Fake<ILogger<AudioEncoder>>(),
            (_, _) => throw new InvalidOperationException("opus encoder unavailable"),
            (_, _) => wavBytes);

        var result = encoder.Encode(Samples, 48_000, AudioFormat.Opus);

        result.Bytes.ShouldBe(wavBytes);
        result.MimeType.ShouldBe(EncodedAudio.WavMimeType);
        result.ActualFormat.ShouldBe(AudioFormat.Wav);
    }

    [Fact]
    public void Encode_WhenBothWritersThrow_RaisesAnAudioException()
    {
        var encoder = new AudioEncoder(
            A.Fake<ILogger<AudioEncoder>>(),
            (_, _) => throw new InvalidOperationException("opus encoder unavailable"),
            (_, _) => throw new InvalidOperationException("wav encoder unavailable"));

        Should.Throw<AudioException>(() => encoder.Encode(Samples, 48_000, AudioFormat.Opus));
    }

    [Fact]
    public void Encode_WithNoOverride_UsesTheRealWriters()
    {
        var encoder = new AudioEncoder(A.Fake<ILogger<AudioEncoder>>());

        encoder.Encode(Samples, 48_000, AudioFormat.Opus).Bytes.ShouldNotBeEmpty();
        encoder.Encode(Samples, 48_000, AudioFormat.Wav).Bytes.ShouldNotBeEmpty();
    }
}
