namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// Defines the modes for initiating and controlling the recording process.
/// </summary>
public enum RecordingMode
{
    /// <summary>
    /// Indicates that recording is active only while the hotkey is being held down.
    /// </summary>
    HoldToRecord,

    /// <summary>
    /// Indicates that recording is toggled on or off with a single press of the hotkey.
    /// </summary>
    Toggle,
}
