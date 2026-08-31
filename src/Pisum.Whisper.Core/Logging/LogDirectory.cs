namespace Pisum.Whisper.Core.Logging;

/// <summary>
/// The resolved location of the log files, and the one place their naming is decided. It is a
/// registered service rather than static, so the settings window can show the path and change 10
/// can open it.
/// </summary>
public sealed class LogDirectory
{
    private const string ApplicationDirectoryName = ".pisum-whisper";

    private const string LogsDirectoryName = "logs";

    /// <summary>The base name Serilog rolls from: <c>pisum-whisper.log</c>, <c>pisum-whisper_001.log</c>, and so on.</summary>
    public const string LogFileName = "pisum-whisper.log";

    /// <summary>Matches the base file and every rolled one — the sequence precedes the extension.</summary>
    public const string LogFileSearchPattern = "pisum-whisper*.log";

    public LogDirectory()
        : this(DefaultPath())
    {
    }

    /// <summary>Constructs over an explicit directory, which is how the tests avoid the real home directory.</summary>
    public LogDirectory(string path)
    {
        Path = path;
    }

    /// <summary>The absolute directory the log files live in, whether any of them exist yet.</summary>
    public string Path { get; }

    public string LogFilePath => System.IO.Path.Combine(Path, LogFileName);

    public static string DefaultPath()
    {
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ApplicationDirectoryName,
            LogsDirectoryName);
    }

    /// <summary>
    /// Creates the directory if it is absent. Returns <c>null</c> once it is usable and the reason it
    /// is not otherwise: a log directory that cannot be created is an inconvenience, not a reason to
    /// refuse to launch a dictation tool.
    /// </summary>
    public string? TryCreate()
    {
        try
        {
            Directory.CreateDirectory(Path);
            return null;
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException
                                              or NotSupportedException)
        {
            return exception.Message;
        }
    }
}
