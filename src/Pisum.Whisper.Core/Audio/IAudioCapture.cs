namespace Pisum.Whisper.Core.Audio;

/// <summary>
/// Captures audio from the system default input device. <see cref="StopAsync"/> returns every sample
/// recorded since <see cref="Start"/>, regardless of duration — no minimum-duration or
/// empty-recording check is applied here; that belongs to whichever component owns the recording
/// state machine.
/// </summary>
public interface IAudioCapture
{
    /// <summary>Opens the default input device and begins capturing.</summary>
    void Start();

    /// <summary>Stops capture and returns every sample recorded since <see cref="Start"/>.</summary>
    Task<float[]> StopAsync();
}
