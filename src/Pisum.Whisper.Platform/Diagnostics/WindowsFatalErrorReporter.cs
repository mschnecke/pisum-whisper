namespace Pisum.Whisper.Platform.Diagnostics;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pisum.Whisper.Core.Diagnostics;

/// <summary>
/// The Windows fatal-error dialog, through plain <c>MessageBoxW</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It pumps its own modal loop</b>, which is the whole reason it is the transport here: it needs
/// neither a window of ours nor a message pump of ours, and every call site is a point at which this
/// process has neither.
/// </para>
/// <para>
/// <c>[SupportedOSPlatform]</c> plus the <c>OperatingSystem.IsWindows()</c> guard in
/// <see cref="NativeFatalErrorReporter.Create"/> is what clears CA1416, exactly as
/// <c>WindowsClipboard</c> already does. The types are in the shared framework; nothing is added to
/// <c>Directory.Packages.props</c> for this.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsFatalErrorReporter : IFatalErrorReporter
{
    /// <summary>
    /// <c>MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST</c>. The last two are what a process
    /// with no window of its own needs in order to be seen at all.
    /// </summary>
    private const uint OkIconErrorForegroundTopmost = 0x00050010;

    public void Report(string title, string message)
    {
        try
        {
            MessageBox(IntPtr.Zero, message, title, OkIconErrorForegroundTopmost);
        }
        catch (Exception)
        {
            // Swallowed on purpose. This runs while a failure is already being handled, and losing
            // the exit code and the log line behind it would be worse than losing the dialog.
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr owner, string text, string caption, uint type);
}
