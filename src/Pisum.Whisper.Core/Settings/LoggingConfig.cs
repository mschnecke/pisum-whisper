namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// Configuration settings for logging behavior, including log level, file size, and retention policies.
/// </summary>
public sealed class LoggingConfig
{
    /// <summary>
    /// Represents the logging verbosity level for the application.
    /// Acceptable values may include predefined levels such as "debug", "info",
    /// "warn", "error", or other custom-defined levels, depending on system configuration.
    /// This property is used to control the amount of detail included in log output.
    /// </summary>
    public string LogLevel { get; set; } = "info";

    /// <summary>
    /// The maximum size, in megabytes, that a log file can reach
    /// before a new log file is created. Used to manage disk space utilization
    /// and maintain log rotation. The default value is 1 MB.
    /// </summary>
    public int LogMaxFileSizeMb { get; set; } = 1;

    /// <summary>
    /// The number of days to retain log files.
    /// This property determines the duration for which log files are retained before being purged.
    /// A higher value ensures logs are available for a longer period, while a lower value reduces storage usage.
    /// </summary>
    public int LogRetentionDays { get; set; } = 7;
}
