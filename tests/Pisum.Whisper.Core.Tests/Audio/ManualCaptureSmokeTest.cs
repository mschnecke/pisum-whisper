namespace Pisum.Whisper.Core.Tests.Audio;

using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Tests;

/// <summary>
/// The spikes never reference <c>Core</c> (see <c>spikes/Pisum.Whisper.Spikes</c>), so this is the
/// only thing that exercises <see cref="MiniAudioCapture"/> end to end against real hardware — there
/// is no microphone in CI. Run manually on both Windows and macOS before this change ships, and play
/// both written files back to confirm they sound right.
/// </summary>
public sealed class ManualCaptureSmokeTest
{
    [Fact(
        Skip = "Requires a real microphone; run manually",
        SkipUnless = nameof(ManualTests.Enabled),
        SkipType = typeof(ManualTests))]
    public async Task RecordFiveSecondsAndWriteBothFormatsForPlayback()
    {
        var capture = new MiniAudioCapture();
        capture.Start();
        await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var samples = await capture.StopAsync();

        var opusBytes = new OggOpusWriter().Write(samples, 48_000);
        var wavBytes = new WavWriter().Write(samples, 48_000);

        var directory = Path.Combine(Path.GetTempPath(), "pisum-whisper-manual-capture");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "capture.opus"), opusBytes, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(directory, "capture.wav"), wavBytes, TestContext.Current.CancellationToken);

        Console.WriteLine(
            $"Captured {samples.Length} samples. Wrote {directory}/capture.opus and capture.wav — play both back.");
    }
}
