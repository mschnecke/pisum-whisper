namespace Pisum.Whisper.Core.Audio;

/// <summary>
/// Captures audio from the system default input device. <see cref="StopAsync"/> returns every sample
/// recorded since <see cref="Start"/>, regardless of duration — no minimum-duration or
/// empty-recording check is applied here; that belongs to whichever component owns the recording
/// state machine.
/// </summary>
public interface IAudioCapture
{
    /// <summary>
    /// The rate, in hertz, every capture is requested at. Fixed by this capability's contract rather
    /// than discovered per recording: the <c>audio-capture</c> spec requires 48 kHz mono to be asked
    /// for and the backend to convert, so there is one value and it lives here rather than in an
    /// implementation the encoder's caller cannot see.
    /// </summary>
    /// <remarks>
    /// It is a constant because handing <see cref="IAudioEncoder.Encode"/> a different number fails
    /// quietly and expensively: Opus permits only 8/12/16/24/48 kHz, so a wrong value throws inside
    /// the encoder, <see cref="AudioEncoder"/> catches broadly by design and falls back to WAV, and
    /// every user silently loses Opus — where the 14 MiB upload ceiling arrives after about 2 min
    /// 33 s instead of 81 minutes.
    /// </remarks>
    public const int SampleRate = 48_000;

    /// <summary>Opens the default input device and begins capturing.</summary>
    void Start();

    /// <summary>Stops capture and returns every sample recorded since <see cref="Start"/>.</summary>
    Task<float[]> StopAsync();
}
