namespace Pisum.Whisper.Core.Logging;

/// <summary>
/// Deletes log files that have outlived the retention window. Size-based rolling caps a single busy
/// session but never removes a small file from six months ago, so both bounds are needed.
/// </summary>
public static class LogRetentionSweep
{
    /// <summary>
    /// Deletes every log file last written before the cutoff and returns their names rather than
    /// logging them: the sweep has to run before the file sink opens — Serilog holds the active file
    /// with <see cref="FileShare.Read"/>, which excludes delete — so it has no logger of its own.
    /// </summary>
    public static IReadOnlyList<string> Run(string directory, int retentionDays)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromDays(retentionDays);
        var removed = new List<string>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, LogDirectory.LogFileSearchPattern))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoff)
                    {
                        continue;
                    }

                    File.Delete(file);
                    removed.Add(Path.GetFileName(file));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // One file another process still holds is not a reason to abandon the rest.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Housekeeping. An unreadable directory is reported by whoever tried to write to it.
        }

        return removed;
    }
}
