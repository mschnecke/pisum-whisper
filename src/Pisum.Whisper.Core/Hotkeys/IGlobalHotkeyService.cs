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

    /// <summary>
    /// Raised when <see cref="Availability"/> changes, carrying the new value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Access can be withdrawn long after startup — <c>libuiohook</c> reports it by ending its run,
    /// which is caught far from the <c>StartAsync</c> that returned successfully — so asking is not
    /// enough. Without this, a binding that stops working is knowable only by opening the settings
    /// window, which is the wrong audience: from where the user sits the application is simply doing
    /// nothing.
    /// </para>
    /// <para>
    /// Raised only on an actual change, so publishing the same state again tells nobody twice.
    /// </para>
    /// <para>
    /// <b>Never raised from the hook thread</b>, and no implementation may start doing so. A
    /// subscriber here draws on screen, and the operating system removes a hook that takes too long
    /// without saying so — reporting from there would cost the user the hotkey in the course of
    /// telling them about it.
    /// </para>
    /// </remarks>
    event EventHandler<HotkeyAvailability>? AvailabilityChanged;

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
