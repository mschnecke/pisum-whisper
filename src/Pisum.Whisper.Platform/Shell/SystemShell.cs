namespace Pisum.Whisper.Platform.Shell;

using System.ComponentModel;
using System.Diagnostics;
using Pisum.Whisper.Core.Shell;

/// <summary>
/// Opens a folder through the operating system's shell.
/// </summary>
/// <remarks>
/// There is deliberately no <c>OperatingSystem.IsWindows()</c> switch here, which makes this the one
/// type in this project that is not a Windows/macOS pair. .NET implements
/// <c>UseShellExecute = true</c> on macOS by handing the path to <c>/usr/bin/open</c> — precisely the
/// command the reference's <c>open_log_folder</c> runs — so one call covers both targets. That is
/// unusual enough here that the macOS half is verified by hand in task 6.2 rather than trusted.
/// </remarks>
public sealed class SystemShell : ISystemShell
{
    public void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) {UseShellExecute = true});
        }
        catch (Exception exception) when (exception is Win32Exception
                                              or ObjectDisposedException
                                              or InvalidOperationException
                                              or PlatformNotSupportedException)
        {
            throw new SystemShellException($"'{path}' could not be opened: {exception.Message}", exception);
        }
    }
}
