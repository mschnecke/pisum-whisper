namespace Pisum.Whisper.Core.Logging;

using Serilog.Events;

/// <summary>
/// The five level names <c>logLevel</c> accepts, matched case-insensitively because the settings
/// file is hand-editable. Serilog's own spellings are deliberately not aliases: the settings window
/// presents a dropdown, so free text only reaches this from a hand-edit, and an unrecognised value
/// falls back to <c>info</c> with a warning that names it.
/// </summary>
public static class LogLevelNames
{
    /// <summary>The level applied when a value is not one of the five.</summary>
    public const LogEventLevel Fallback = LogEventLevel.Information;

    /// <summary>Maps a settings value to a Serilog level, reporting whether it was recognised.</summary>
    public static bool TryParse(string? name, out LogEventLevel level)
    {
        switch (name?.ToLowerInvariant())
        {
            case "trace":
                level = LogEventLevel.Verbose;
                return true;
            case "debug":
                level = LogEventLevel.Debug;
                return true;
            case "info":
                level = LogEventLevel.Information;
                return true;
            case "warn":
                level = LogEventLevel.Warning;
                return true;
            case "error":
                level = LogEventLevel.Error;
                return true;
            default:
                level = Fallback;
                return false;
        }
    }
}
