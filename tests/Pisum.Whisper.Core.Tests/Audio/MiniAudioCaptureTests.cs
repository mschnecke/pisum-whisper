namespace Pisum.Whisper.Core.Tests.Audio;

using System.Threading.Channels;
using Pisum.Whisper.Core.Audio;
using Shouldly;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MiniAudioCaptureTests
{
    /// <summary>
    /// Task 1.1. The capture rate is a contract value, not a tuning knob: it is what
    /// <see cref="IAudioEncoder.Encode"/> is handed for every recording, and a wrong value does not
    /// fail — Opus rejects it, the encoder falls back to WAV with one warning, and the upload
    /// ceiling arrives after two and a half minutes instead of eighty-one. Pinning it here makes a
    /// change to it deliberate.
    /// </summary>
    [Fact]
    public void TheCaptureRateIs48Khz()
    {
        IAudioCapture.SampleRate.ShouldBe(48_000);
    }

    [Fact]
    public async Task DrainAsync_ConcatenatesChunksInWriteOrder()
    {
        var channel = Channel.CreateUnbounded<float[]>();
        await channel.Writer.WriteAsync([1f, 2f], TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync([3f], TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync([4f, 5f, 6f], TestContext.Current.CancellationToken);
        channel.Writer.Complete();

        var samples = await MiniAudioCapture.DrainAsync(channel.Reader);

        samples.ShouldBe([1f, 2f, 3f, 4f, 5f, 6f]);
    }

    [Fact]
    public async Task DrainAsync_WhenTheChannelCompletesWithNoChunksWritten_ReturnsEmpty()
    {
        var channel = Channel.CreateUnbounded<float[]>();
        channel.Writer.Complete();

        var samples = await MiniAudioCapture.DrainAsync(channel.Reader);

        samples.ShouldBeEmpty();
    }
}
