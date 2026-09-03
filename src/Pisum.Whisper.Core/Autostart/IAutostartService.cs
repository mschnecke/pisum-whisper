namespace Pisum.Whisper.Core.Autostart;

/// <summary>
/// The machine's login-item registration for this application, as far as this application needs one.
/// </summary>
/// <remarks>
/// Methods rather than a property, because all three do I/O: on Windows they read and write a value
/// under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, on macOS a LaunchAgent plist in
/// <c>~/Library/LaunchAgents</c>. A property that touched the registry on every get would read as
/// though it did not.
/// </remarks>
public interface IAutostartService
{
    /// <summary>
    /// What the login registration says: nothing, something that is not this application's current
    /// registration, or this application's current registration.
    /// </summary>
    /// <remarks>
    /// <b>Not a boolean.</b> <c>Current</c> means the registration would be written identically by
    /// <see cref="Enable"/> right now, so a registration naming a different executable reads as
    /// <see cref="AutostartRegistration.Stale"/> rather than as "enabled" — which is the only thing
    /// that lets <see cref="AutostartReconciler"/> repoint it. See
    /// <see cref="AutostartRegistration"/>.
    /// </remarks>
    /// <exception cref="AutostartException">The registration could not be read.</exception>
    AutostartRegistration Read();

    /// <summary>
    /// Registers this application to start at login. Registering while already registered leaves one
    /// entry, not two.
    /// </summary>
    /// <exception cref="AutostartException">The registration could not be created.</exception>
    void Enable();

    /// <summary>Unregisters this application. Unregistering when not registered does nothing.</summary>
    /// <exception cref="AutostartException">The registration could not be removed.</exception>
    void Disable();
}
