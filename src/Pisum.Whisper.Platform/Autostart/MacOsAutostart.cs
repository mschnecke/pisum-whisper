namespace Pisum.Whisper.Platform.Autostart;

using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;
using Pisum.Whisper.Core.Autostart;

/// <summary>
/// Start at login on macOS: a LaunchAgent plist in the user's own <c>~/Library/LaunchAgents</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>No <c>launchctl</c>.</b> The reference's <c>tauri-plugin-autostart</c> writes the plist and
/// nothing else under <c>MacosLauncher::LaunchAgent</c>, because <c>launchd</c> reads the directory
/// at login. Shelling out would add a process and a second failure mode for no behaviour.
/// </para>
/// <para>
/// <b>No <c>SMAppService</c>.</b> It needs macOS 13 and an application bundle, which change 12 has
/// not built yet; the plist matches the reference and works further back.
/// </para>
/// <para>
/// <b>The directory is injected</b>, defaulting to the real one — which is what makes the file
/// format and the enable/disable/re-enable cycle testable from any operating system, leaving only
/// the effect on login to hardware.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacOsAutostart : IAutostartService
{
    /// <summary>The reverse-DNS identity, and the plist's file name without its extension.</summary>
    public const string Label = "net.pisum.whisper";

    private const string FileName = $"{Label}.plist";

    private readonly string _directory;

    public MacOsAutostart()
        : this(DefaultDirectory())
    {
    }

    /// <summary>Constructs the service over an explicit directory, which is how the tests avoid the real one.</summary>
    public MacOsAutostart(string directory)
    {
        _directory = directory;
    }

    public static string DefaultDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents");
    }

    /// <summary>The plist this service writes, so a test need not hard-code the path twice.</summary>
    public string PlistPath => Path.Combine(_directory, FileName);

    public AutostartRegistration Read()
    {
        try
        {
            if (!File.Exists(PlistPath))
            {
                return AutostartRegistration.Absent;
            }

            // The whole file rather than the ProgramArguments element alone, and so no parser: the
            // question is whether what is on disk is what Enable would write now, and comparing the
            // text answers it for the executable path, the label and the format at once. A plist
            // that differs only cosmetically is rewritten into the canonical one, which is harmless
            // — Enable overwrites — and leaves the comparison stable from then on.
            // Environment.ProcessPath is null only for a host that cannot name its own executable,
            // and an agent cannot be claimed as ours without it: Stale rather than a throw, so the
            // setting being off can still remove it.
            return Environment.ProcessPath is { } path
                   && File.ReadAllText(PlistPath) == Plist(path)
                ? AutostartRegistration.Current
                : AutostartRegistration.Stale;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new AutostartException(
                $"The login agent at '{PlistPath}' could not be read: {exception.Message}",
                exception);
        }
    }

    public void Enable()
    {
        try
        {
            Directory.CreateDirectory(_directory);

            // Overwritten rather than appended to, so enabling twice leaves one agent — and
            // repointing a stale one needs no separate delete.
            File.WriteAllText(PlistPath, Plist(ExecutablePath()), new UTF8Encoding(false));
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new AutostartException(
                $"The login agent at '{PlistPath}' could not be created: {exception.Message}",
                exception);
        }
    }

    public void Disable()
    {
        try
        {
            File.Delete(PlistPath);
        }
        catch (DirectoryNotFoundException)
        {
            // There is no LaunchAgents directory, so there is no agent in it. File.Delete already
            // treats a missing file as a no-op; a missing directory is the same thing one level up,
            // and unregistering what was never registered has to do nothing either way.
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new AutostartException(
                $"The login agent at '{PlistPath}' could not be removed: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// The three keys <c>launchd</c> needs from a login agent: who it is, what to run, and that it
    /// runs at load.
    /// </summary>
    /// <remarks>
    /// Built through <see cref="XDocument"/> rather than string-concatenated, so a path containing
    /// an ampersand or an angle bracket cannot produce a plist <c>launchd</c> refuses to parse. The
    /// doctype is what makes it a plist rather than bare XML.
    /// </remarks>
    private static string Plist(string executablePath)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType(
                "plist",
                "-//Apple//DTD PLIST 1.0//EN",
                "http://www.apple.com/DTDs/PropertyList-1.0.dtd",
                null),
            new XElement(
                "plist",
                new XAttribute("version", "1.0"),
                new XElement(
                    "dict",
                    new XElement("key", "Label"),
                    new XElement("string", Label),
                    new XElement("key", "ProgramArguments"),
                    new XElement("array", new XElement("string", executablePath)),
                    new XElement("key", "RunAtLoad"),
                    new XElement("true"))));

        return document.Declaration + Environment.NewLine + document;
    }

    /// <remarks>
    /// Under <c>dotnet run</c> this is a raw apphost in the build output; an installed build names
    /// the executable inside <c>/Applications/Pisum Whisper.app</c>. <see cref="AutostartReconciler"/>
    /// rewrites the one into the other on the first launch after an install, which is what
    /// <see cref="AutostartRegistration.Stale"/> exists for.
    /// </remarks>
    private static string ExecutablePath()
    {
        return Environment.ProcessPath
               ?? throw new AutostartException(
                   "The path of the running executable could not be determined, so there is nothing to register.");
    }

    private static bool IsFileSystemFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException;
    }
}
