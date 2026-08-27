using System.Text;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;

namespace Pisum.Whisper.Spikes;

/// <summary>
/// S4 (task 1.7) — encode the S2 capture to Ogg/Opus and prove the container is well formed.
/// The open question "does Concentus.Oggfile work with Concentus 2.x" was already answered from
/// package metadata, so this checks header correctness and decodability instead: no ffmpeg is
/// installed, so the file is verified by decoding it back with the library's own reader.
/// </summary>
internal static class OpusSpike
{
    public static Task<int> RunAsync()
    {
        var rawPath = Path.Combine(Path.GetTempPath(), "pisum-spike-capture.f32");
        if (!File.Exists(rawPath))
        {
            Console.WriteLine($"missing {rawPath} - run the 'audio' spike first");
            return Task.FromResult(3);
        }

        var bytes = File.ReadAllBytes(rawPath);
        var samples = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        var sourceSeconds = samples.Length / (double)AudioSpike.TargetRate;
        Console.WriteLine($"source: {samples.Length} f32 mono samples @ {AudioSpike.TargetRate} Hz = {sourceSeconds:F2}s");

        var oggPath = Path.Combine(Path.GetTempPath(), "pisum-spike-capture.opus");
        var encoder = OpusCodecFactory.CreateEncoder(AudioSpike.TargetRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = 24_000;

        using (var fs = File.Create(oggPath))
        {
            var ogg = new OpusOggWriteStream(encoder, fs, new OpusTags(), AudioSpike.TargetRate, 5, false);
            ogg.WriteSamples(samples, 0, samples.Length);
            ogg.Finish();
        }

        var written = new FileInfo(oggPath).Length;
        var head = File.ReadAllBytes(oggPath).AsSpan(0, Math.Min(128, (int)written));
        var asAscii = Encoding.ASCII.GetString(head.ToArray());

        var capturePattern = asAscii.IndexOf("OpusHead", StringComparison.Ordinal);
        int preSkip = -1, channels = -1, headVersion = -1;
        if (capturePattern >= 0)
        {
            headVersion = head[capturePattern + 8];
            channels = head[capturePattern + 9];
            preSkip = head[capturePattern + 10] | (head[capturePattern + 11] << 8);
        }

        Console.WriteLine($"\nwritten          : {oggPath} ({written:N0} bytes)");
        Console.WriteLine($"kbps (actual)    : {written * 8 / sourceSeconds / 1000:F1}");
        Console.WriteLine($"capture pattern  : OggS={asAscii.StartsWith("OggS")}  OpusHead={capturePattern >= 0}  OpusTags={asAscii.Contains("OpusTags")}");
        Console.WriteLine($"OpusHead version : {headVersion}");
        Console.WriteLine($"OpusHead channels: {channels}");
        Console.WriteLine($"OpusHead pre-skip: {preSkip}   (add-audio-pipeline's proposal specifies 312)");

        // Round trip: decode with the library's own reader.
        var decoder = OpusCodecFactory.CreateDecoder(AudioSpike.TargetRate, 1);
        using var readFs = File.OpenRead(oggPath);
        var reader = new OpusOggReadStream(decoder, readFs);
        var decoded = 0;
        var packets = 0;
        while (reader.HasNextPacket)
        {
            var pcm = reader.DecodeNextPacket();
            if (pcm is null) break;
            decoded += pcm.Length;
            packets++;
        }

        var decodedSeconds = decoded / (double)AudioSpike.TargetRate;
        Console.WriteLine($"\nround trip       : {packets} packets, {decoded} samples = {decodedSeconds:F2}s");
        Console.WriteLine($"reader TotalTime : {reader.TotalTime}");
        Console.WriteLine($"granule count    : {reader.GranuleCount}");
        Console.WriteLine($"duration match   : {decodedSeconds / sourceSeconds:P1} of source");

        var pass = asAscii.StartsWith("OggS") && capturePattern >= 0 && asAscii.Contains("OpusTags")
                   && packets > 0 && decodedSeconds / sourceSeconds is > 0.97 and < 1.03;
        Console.WriteLine($"\nS4 VERDICT: {(pass ? "PASS - well-formed Ogg/Opus, decodes back to the same duration" : "FAIL")}");
        return Task.FromResult(pass ? 0 : 1);
    }
}
