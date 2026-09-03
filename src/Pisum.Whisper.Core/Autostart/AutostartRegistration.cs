namespace Pisum.Whisper.Core.Autostart;

/// <summary>
/// What the machine's login registration currently says, as far as reconciling it needs to know.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three states rather than a boolean, because there are three cases and a boolean hides one.</b>
/// A registration that exists but names a different executable is neither absent nor correct: it is
/// a registration that will launch something else at login — the previous install, or a build output
/// left behind by a developer — while <c>startWithSystem</c> reports that everything is in order.
/// Answering "is it registered" with one bit is what let that state survive every save.
/// </para>
/// <para>
/// One read answers both questions, which is what keeps
/// <see cref="AutostartReconciler"/> at one registry read per save.
/// </para>
/// </remarks>
public enum AutostartRegistration
{
    /// <summary>Nothing is registered.</summary>
    Absent,

    /// <summary>
    /// Something is registered, but it is not what <see cref="IAutostartService.Enable"/> would write
    /// now — most usually because it names an executable this process is not.
    /// </summary>
    Stale,

    /// <summary>A registration exists and names the running executable.</summary>
    Current,
}
