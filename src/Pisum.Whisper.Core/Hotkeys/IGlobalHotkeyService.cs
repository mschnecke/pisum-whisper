namespace Pisum.Whisper.Core.Hotkeys;

/// <summary>
/// Observes the configured key combination system-wide and reports both of its edges.
/// </summary>
/// <remarks>
/// <para>
/// The events are raised on a dedicated dispatch thread, never on the hook's own thread, so a
/// handler may take as long as it needs — opening a microphone, for instance. Edges are delivered in
/// order and a <see cref="Released"/> always follows the <see cref="Pressed"/> it belongs to, even
/// when observation ends before the physical key release is seen.
/// </para>
/// <para>
/// This says nothing about recording. Whether a press starts or stops a dictation is the recording
/// state machine's business, and hold-versus-toggle is interpreted there.
/// </para>
/// </remarks>
public interface IGlobalHotkeyService
{
    /// <summary>Raised when the configured combination becomes fully held.</summary>
    event EventHandler? Pressed;

    /// <summary>Raised when the configured combination stops being fully held.</summary>
    event EventHandler? Released;

    /// <summary>Whether the binding is being observed, and if not, why.</summary>
    HotkeyAvailability Availability { get; }

    /// <summary>The binding currently being observed.</summary>
    HotkeyChord Chord { get; }

    /// <summary>
    /// Reports the next complete combination the user presses, for the settings window's recorder.
    /// While a capture is in progress the configured binding is not matched and neither edge is
    /// raised; normal matching resumes when the capture ends, however it ends.
    /// </summary>
    /// <remarks>
    /// Capture reuses this observation rather than starting a second one: libuiohook keeps one
    /// static callback per process, so two concurrent hooks corrupt its internal state.
    /// </remarks>
    Task<HotkeyCapture> CaptureAsync(CancellationToken cancellationToken);
}
