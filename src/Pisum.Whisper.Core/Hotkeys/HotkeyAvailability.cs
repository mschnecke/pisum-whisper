namespace Pisum.Whisper.Core.Hotkeys;

/// <summary>
/// Whether the binding is being observed, and if not, why. The two permission states are kept apart
/// because their remedies differ: one needs a grant and a relaunch, the other needs the grant back.
/// </summary>
public enum HotkeyAvailability
{
    /// <summary>Observation has not been started yet.</summary>
    NotStarted,

    /// <summary>The binding is being observed.</summary>
    Available,

    /// <summary>
    /// The operating system has never granted the access needed to observe keys system-wide. On
    /// macOS this is the Accessibility grant, which cannot be present on a first launch.
    /// </summary>
    PermissionNotGranted,

    /// <summary>
    /// The access was withdrawn after observation had started, either by the user revoking it or by
    /// macOS disabling an unresponsive event tap.
    /// </summary>
    PermissionRevoked,

    /// <summary>Observation could not be started for a reason unrelated to permissions.</summary>
    Failed,
}
