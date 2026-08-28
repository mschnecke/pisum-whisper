namespace Pisum.Whisper.Core.Tests.Audio;

using Pisum.Whisper.Core.Audio;

/// <summary>
/// The spikes never reference <c>Core</c> (see <c>spikes/Pisum.Whisper.Spikes</c>), so this is the
/// only thing that exercises <see cref="MiniAudioCapture"/> end to end against real hardware — there
/// is no microphone in CI. Run manually on both Windows and macOS before this change ships, and play
/// both written files back to confirm they sound right.
/// </summary>
[TestClass]
[Ignore("Requires a real microphone; run manually")]
public sealed class ManualCaptureSmokeTest
{
    [TestMethod]
    public async Task RecordFiveSecondsAndWriteBothFormatsForPlayback()
    {
        var capture = new MiniAudioCapture();
        capture.Start();
        await Task.Delay(TimeSpan.FromSeconds(5));
        var samples = await capture.StopAsync();

        var opusBytes = new OggOpusWriter().Write(samples, 48_000);
        var wavBytes = new WavWriter().Write(samples, 48_000);

        var directory = Path.Combine(Path.GetTempPath(), "pisum-whisper-manual-capture");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "capture.opus"), opusBytes);
        await File.WriteAllBytesAsync(Path.Combine(directory, "capture.wav"), wavBytes);

        Console.WriteLine(
            $"Captured {samples.Length} samples. Wrote {directory}/capture.opus and capture.wav — play both back.");
    }
}
