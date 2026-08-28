namespace Pisum.Whisper.Core.Tests.Audio;

using System.Threading.Channels;
using Pisum.Whisper.Core.Audio;
using Shouldly;

[TestClass]
public sealed class MiniAudioCaptureTests
{
    [TestMethod]
    public async Task DrainAsync_ConcatenatesChunksInWriteOrder()
    {
        var channel = Channel.CreateUnbounded<float[]>();
        await channel.Writer.WriteAsync([1f, 2f]);
        await channel.Writer.WriteAsync([3f]);
        await channel.Writer.WriteAsync([4f, 5f, 6f]);
        channel.Writer.Complete();

        var samples = await MiniAudioCapture.DrainAsync(channel.Reader);

        samples.ShouldBe([1f, 2f, 3f, 4f, 5f, 6f]);
    }

    [TestMethod]
    public async Task DrainAsync_WhenTheChannelCompletesWithNoChunksWritten_ReturnsEmpty()
    {
        var channel = Channel.CreateUnbounded<float[]>();
        channel.Writer.Complete();

        var samples = await MiniAudioCapture.DrainAsync(channel.Reader);

        samples.ShouldBeEmpty();
    }
}
