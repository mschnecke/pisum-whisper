namespace Pisum.Whisper.Core.Dictation;

/// <summary>
/// What the application is doing about a dictation, reported to whatever presents it.
/// </summary>
/// <remarks>
/// <para>
/// Three values rather than the reference's single boolean. The reference calls
/// <c>tray::set_recording_state(false)</c> at the end of its pipeline thread, after the paste
/// (<c>hotkey/manager.rs:329</c>), so its icon claims to be recording throughout the upload — and a
/// user in toggle mode who presses stop, sees the icon unchanged and presses again is told
/// "Transcription In Progress" by an icon that just told them they were still recording.
/// </para>
/// <para>
/// This is not extra surface for its own sake: <see cref="DictationOrchestrator"/> must already tell
/// the two apart, because a hotkey press means different things in each. Publishing a boolean would
/// mean <em>adding</em> a step that discards what is already known.
/// </para>
/// </remarks>
public enum DictationState
{
    /// <summary>Nothing is in progress; the hotkey will start a recording.</summary>
    Idle,

    /// <summary>The microphone is open and audio is being captured. The user should keep speaking.</summary>
    Recording,

    /// <summary>
    /// The recording is being encoded, transcribed and delivered. The user should stop speaking, and
    /// the hotkey will not start a second dictation.
    /// </summary>
    Transcribing,
}
