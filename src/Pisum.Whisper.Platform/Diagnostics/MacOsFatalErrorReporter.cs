namespace Pisum.Whisper.Platform.Diagnostics;

using System.Diagnostics;
using System.Runtime.Versioning;
using Pisum.Whisper.Core.Diagnostics;

/// <summary>
/// The macOS fatal-error dialog, through <c>osascript</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the transport change 11 rejected, and the rejection does not carry.</b> Of its three
/// objections to <c>osascript -e 'display notification'</c>, only the first survives for a dialog
/// raised while the process is already dying: the dialog is attributed to Script Editor, which is
/// accepted because the alternative is nothing at all. The other two do not apply — with no
/// dispatcher there is nothing to draw a window with, and every call site here is the main thread
/// with no hotkey left to protect.
/// </para>
/// <para>
/// <c>NSAlert</c> through the Objective-C runtime — which <c>MacOsClipboard</c> establishes as a
/// technique in this project — needs <c>NSApplication.sharedApplication</c> and a run loop, and one
/// of the four call sites is Avalonia having failed to give us either.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacOsFatalErrorReporter : IFatalErrorReporter
{
    public void Report(string title, string message)
    {
        try
        {
            var script = $"display dialog \"{AppleScript.Escape(message)}\" with title \"{AppleScript.Escape(title)}\" "
                         + "buttons {\"OK\"} default button \"OK\" with icon stop";

            using var osascript = Process.Start("/usr/bin/osascript", ["-e", script]);
            osascript?.WaitForExit();
        }
        catch (Exception)
        {
            // Swallowed on purpose, for the same reason as the Windows half: a missing
            // /usr/bin/osascript must not cost the exit code or the log line behind it.
        }
    }
}
