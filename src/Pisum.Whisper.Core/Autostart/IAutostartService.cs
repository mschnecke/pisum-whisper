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
    /// <summary>Whether this application is registered to start at login.</summary>
    /// <exception cref="AutostartException">The registration could not be read.</exception>
    bool IsEnabled();

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
