namespace Pisum.Whisper.Core.Audio;

using System.Threading.Channels;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Enums;
using SFAudioFormat = SoundFlow.Structs.AudioFormat;

/// <summary>
/// Captures 48 kHz mono audio from the system default input device via miniaudio (SoundFlow), which
/// converts from whatever rate and channel count the device natively runs at — see spike S2 in
/// <c>openspec/changes/archive/2026-08-27-bootstrap-solution/design.md</c>. Each buffer from the
/// realtime callback is copied into an unbounded channel rather than appended under a lock, so the
/// callback thread never blocks.
/// </summary>
public sealed class MiniAudioCapture : IAudioCapture
{
    private const int SampleRate = 48_000;

    private MiniAudioEngine? _engine;
    private AudioCaptureDevice? _device;
    private Channel<float[]>? _channel;

    public void Start()
    {
        var engine = new MiniAudioEngine();
        engine.UpdateAudioDevicesInfo();

        if (engine.CaptureDevices.Length == 0)
        {
            throw new AudioException("No input device found");
        }

        var deviceInfo = engine.CaptureDevices.First(d => d.IsDefault);
        var format = new SFAudioFormat { SampleRate = SampleRate, Channels = 1, Format = SampleFormat.F32 };
        var device = engine.InitializeCaptureDevice(deviceInfo, format, new MiniAudioDeviceConfig());
        var channel = Channel.CreateUnbounded<float[]>();

        device.OnAudioProcessed += (samples, _) => channel.Writer.TryWrite(samples.ToArray());
        device.Start();

        _engine = engine;
        _device = device;
        _channel = channel;
    }

    public async Task<float[]> StopAsync()
    {
        if (_engine is null || _device is null || _channel is null)
        {
            throw new InvalidOperationException("Capture was not started.");
        }

        _device.Stop();
        _channel.Writer.Complete();

        var samples = await DrainAsync(_channel.Reader).ConfigureAwait(false);

        _device.Dispose();
        _engine.Dispose();
        _engine = null;
        _device = null;
        _channel = null;

        return samples;
    }

    /// <summary>Concatenates every chunk written before the channel completed, in write order.</summary>
    internal static async Task<float[]> DrainAsync(ChannelReader<float[]> reader)
    {
        var chunks = new List<float[]>();
        await foreach (var chunk in reader.ReadAllAsync().ConfigureAwait(false))
        {
            chunks.Add(chunk);
        }

        var samples = new float[chunks.Sum(c => c.Length)];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            chunk.CopyTo(samples, offset);
            offset += chunk.Length;
        }

        return samples;
    }
}
