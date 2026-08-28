namespace Pisum.Whisper.Core.Settings;

/// <summary>File logging limits. Consumed by the logging change, which owns their interpretation.</summary>
public sealed class LoggingConfig
{
    public string LogLevel { get; set; } = "info";

    public int LogMaxFileSizeMb { get; set; } = 1;

    public int LogRetentionDays { get; set; } = 7;
}
