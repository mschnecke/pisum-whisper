namespace Pisum.Whisper.Core.Shell;

/// <summary>
/// The operating system's file browser, as far as this application needs one.
/// </summary>
/// <remarks>
/// It exists so the Logging tab's view model can be tested: <c>Process.Start</c> cannot be faked,
/// and without a seam the Open Log Folder command would have no test at all. It is not a placeholder
/// for a second implementation.
/// </remarks>
public interface ISystemShell
{
    /// <summary>Opens <paramref name="path"/> in the operating system's file browser.</summary>
    /// <exception cref="SystemShellException">The file browser could not be launched.</exception>
    void OpenFolder(string path);
}
