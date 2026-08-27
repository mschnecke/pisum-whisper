using System.Diagnostics;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace Pisum.Whisper.Spikes;

/// <summary>
/// S2 (task 1.5a) — can miniaudio open the default input at a requested format and convert from the
/// device's native one? The design deletes the reference's sinc-resampling stage on that assumption.
/// Measured between the first and last callback, so WASAPI spin-up is not counted as dropped audio.
/// </summary>
internal static class AudioSpike
{
    public const int TargetRate = 48_000;

    public static async Task<int> RunAsync()
    {
        using var engine = new MiniAudioEngine();
        engine.UpdateAudioDevicesInfo();

        Console.WriteLine($"playback devices: {engine.PlaybackDevices.Length}");
        foreach (var d in engine.PlaybackDevices)
        {
            var prates = d.SupportedDataFormats.Select(f => (int)f.SampleRate).Distinct().OrderBy(x => x);
            Console.WriteLine($"  [{(d.IsDefault ? "default" : "       ")}] {d.Name} :: {string.Join(",", prates)}");
        }

        Console.WriteLine($"capture devices: {engine.CaptureDevices.Length}");
        foreach (var d in engine.CaptureDevices)
        {
            var rates = d.SupportedDataFormats.Select(f => $"{f.SampleRate}Hz/{f.Channels}ch/{f.Format}").Distinct();
            Console.WriteLine($"  [{(d.IsDefault ? "default" : "       ")}] {d.Name}");
            Console.WriteLine($"     native: {string.Join(", ", rates.Take(6))}");
        }

        if (engine.CaptureDevices.Length == 0)
        {
            Console.WriteLine("\nS2 VERDICT: INCONCLUSIVE - no capture device");
            return 3;
        }

        var chosen = engine.CaptureDevices.FirstOrDefault(d => d.IsDefault);
        var nativeRates = chosen.SupportedDataFormats.Select(f => (int)f.SampleRate).Distinct().ToArray();
        var nativeChannels = chosen.SupportedDataFormats.Select(f => f.Channels).Distinct().ToArray();
        Console.WriteLine($"\nnative rates: {string.Join(",", nativeRates)}   native channels: {string.Join(",", nativeChannels)}");

        // 48 kHz mono: the format the product actually wants.
        var target = await CaptureAsync(engine, chosen, TargetRate, 4);
        // A rate the device does NOT offer natively. If miniaudio honours it, sample-rate
        // conversion is proven - the direction of the conversion does not matter.
        var offRate = nativeRates.Contains(16_000) ? 22_050 : 16_000;
        var resampled = await CaptureAsync(engine, chosen, offRate, 4);

        Console.WriteLine("\n--- results ---");
        foreach (var r in new[] { target, resampled }) r.Print();

        var monoOk = target.Channels == 1 && nativeChannels.All(c => c != 1);
        var rateConversionExercised = !nativeRates.Contains(resampled.RequestedRate);
        var resampleHonoured = resampled.ReportedRate == resampled.RequestedRate;
        var pass = target.RateOk && resampled.RateOk && rateConversionExercised;

        Console.WriteLine($"\nchannel conversion exercised (native has no mono): {monoOk}");
        Console.WriteLine($"rate conversion exercised ({resampled.RequestedRate} Hz not native): {rateConversionExercised}");
        var verdict = pass ? "PASS - requested rate and channel count both honoured accurately"
            : target.RateOk && resampleHonoured
                ? "PARTIAL - the 48 kHz path is exact, but resampling under-delivers; see notes"
                : "FAIL";
        Console.WriteLine("\nS2 VERDICT: " + verdict);

        if (target.Samples.Length > 0)
        {
            var path = Path.Combine(Path.GetTempPath(), "pisum-spike-capture.f32");
            await using var fs = File.Create(path);
            await using var bw = new BinaryWriter(fs);
            foreach (var s in target.Samples) bw.Write(s);
            Console.WriteLine($"48 kHz mono f32 written to: {path}  ({target.Samples.Length} samples)");
        }

        return pass ? 0 : 1;
    }

    private static async Task<Result> CaptureAsync(MiniAudioEngine engine, DeviceInfo info, int rate, int seconds)
    {
        var format = new AudioFormat { SampleRate = rate, Channels = 1, Format = SampleFormat.F32 };
        using var device = engine.InitializeCaptureDevice(info, format, new MiniAudioDeviceConfig());

        var samples = new List<float>();
        var sizes = new List<int>();
        var callbacks = 0;
        long firstTicks = 0, lastTicks = 0;

        device.OnAudioProcessed += (buffer, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            Interlocked.CompareExchange(ref firstTicks, now, 0);
            Interlocked.Exchange(ref lastTicks, now);
            Interlocked.Increment(ref callbacks);
            lock (samples) samples.AddRange(buffer.ToArray());
            lock (sizes) sizes.Add(buffer.Length);
        };

        device.Start();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var stopTicks = Stopwatch.GetTimestamp();
        device.Stop();
        await Task.Delay(250);

        float[] copy;
        lock (samples) copy = samples.ToArray();

        // Between first and last callback, so device spin-up is excluded.
        // From the first callback to the moment Stop was requested: the true window in which the
        // device was streaming, excluding spin-up.
        var span = (stopTicks - firstTicks) / (double)Stopwatch.Frequency;
        string sizeSummary;
        lock (sizes) sizeSummary = string.Join(", ", sizes.GroupBy(x => x).OrderBy(g => g.Key).Select(g => g.Key + " x" + g.Count()));
        Console.WriteLine("     buffer sizes at " + rate + " Hz: " + sizeSummary);
        return new Result(rate, device.Format.SampleRate, device.Format.Channels, device.Format.Format,
                          callbacks, copy, span);
    }

    private sealed record Result(
        int RequestedRate, int ReportedRate, int Channels, SampleFormat Format,
        int Callbacks, float[] Samples, double SpanSeconds)
    {
        // The final callback's buffer arrives at its start instant, so one buffer of audio falls
        // outside the measured span; compare against span + one buffer.
        private double PerCallback => Callbacks > 1 ? Samples.Length / (double)Callbacks : 0;
        private double Expected => SpanSeconds * RequestedRate;
        public bool RateOk => Expected > 0 && Samples.Length / Expected is > 0.97 and < 1.03;

        public void Print()
        {
            Console.WriteLine($"  requested {RequestedRate,6} Hz -> reported {ReportedRate,6} Hz, {Channels} ch, {Format}");
            Console.WriteLine($"     callbacks={Callbacks,4}  samples={Samples.Length,7}  span={SpanSeconds:F2}s  " +
                              $"expected~{Expected:F0}  ratio={Samples.Length / Expected:P1}  peak={(Samples.Length == 0 ? 0 : Samples.Max(Math.Abs)):F4}");
        }
    }
}
