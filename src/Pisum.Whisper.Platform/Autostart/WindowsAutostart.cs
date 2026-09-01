namespace Pisum.Whisper.Platform.Autostart;

using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using Pisum.Whisper.Core.Autostart;

/// <summary>
/// Start at login on Windows: one value under the current user's <c>Run</c> key.
/// </summary>
/// <remarks>
/// <para>
/// <c>HKEY_CURRENT_USER</c> and never <c>HKEY_LOCAL_MACHINE</c>. The setting is a per-user
/// preference stored in a per-user settings file, and a dictation tool has no business starting for
/// someone who never asked for it — nor any business prompting for elevation to say so.
/// </para>
/// <para>
/// <b><c>Microsoft.Win32.Registry</c> needs no package reference.</b> The types resolve from the
/// shared framework on <c>net10.0</c>; the only diagnostics are <c>CA1416</c>, cleared by
/// <see cref="SupportedOSPlatformAttribute"/> here plus the <c>OperatingSystem.IsWindows()</c> guard
/// in <c>AddNativeAutostart</c> — exactly the pattern <c>WindowsClipboard</c> already uses.
/// </para>
/// <para>
/// <b>The subkey path is injected</b>, defaulting to the real one. Without that the only honest test
/// writes to the user's own <c>Run</c> key, which means a manual test and a capability verified by
/// hand for ever; with it, a test writes to a private subkey and deletes it afterwards.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostart : IAutostartService
{
    /// <summary>Where Windows itself looks for per-user login items.</summary>
    public const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// The value name, and therefore what the user sees in Task Manager's Startup tab and in
    /// Settings. It names the product rather than the executable for that reason.
    /// </summary>
    private const string ValueName = "Pisum Whisper";

    private readonly string _subKey;

    public WindowsAutostart()
        : this(RunSubKey)
    {
    }

    /// <summary>Constructs the service over an explicit subkey, which is how the tests avoid the real Run key.</summary>
    public WindowsAutostart(string subKey)
    {
        _subKey = subKey;
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_subKey);

            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            throw new AutostartException(
                $@"The login registration under HKCU\{_subKey} could not be read: {exception.Message}",
                exception);
        }
    }

    public void Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_subKey);

            // Quoted, because a path under "Program Files" would otherwise be read as a command and
            // its first space as an argument separator. SetValue replaces, so enabling twice leaves
            // one value rather than two.
            key.SetValue(ValueName, $"\"{ExecutablePath()}\"", RegistryValueKind.String);
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            throw new AutostartException(
                $@"The login registration under HKCU\{_subKey} could not be created: {exception.Message}",
                exception);
        }
    }

    public void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_subKey, writable: true);

            // throwOnMissingValue defaults to false, so removing what is not there is a no-op.
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            throw new AutostartException(
                $@"The login registration under HKCU\{_subKey} could not be removed: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// What the login registration points at.
    /// </summary>
    /// <remarks>
    /// Whatever the process was launched as, with no heuristic about what it looks like. Under
    /// <c>dotnet run</c> that is the build output, which is a developer's own doing and is recorded
    /// in the change's <i>Risks</i> rather than guessed at here.
    /// </remarks>
    private static string ExecutablePath()
    {
        return Environment.ProcessPath
               ?? throw new AutostartException(
                   "The path of the running executable could not be determined, so there is nothing to register.");
    }

    /// <summary>
    /// What a registry key that will not answer looks like: a policy, an ACL, a hive that has been
    /// removed. Anything else is a defect here and is left to propagate.
    /// </summary>
    private static bool IsRegistryFailure(Exception exception)
    {
        return exception is UnauthorizedAccessException
            or SecurityException
            or IOException
            or ObjectDisposedException;
    }
}
