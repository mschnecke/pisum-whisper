namespace Pisum.Whisper.Core.Logging;

using System.Text.Json;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// A read-only look at <see cref="LoggingConfig"/> taken before the container — and therefore before
/// <see cref="SettingsStore"/> — exists. It is what lets the process be logged from its first line
/// rather than from whenever settings happen to load.
/// </summary>
/// <remarks>
/// It never writes and never throws, so it cannot itself need logging. <see cref="SettingsStore"/>
/// stays the owner of the file: it is the one that creates it, repairs it and reports on it.
/// </remarks>
public static class LoggingConfigPeek
{
    public static LoggingConfig Read() => Read(SettingsStore.DefaultFilePath());

    /// <summary>Reads from an explicit file, which is how the tests avoid the real home directory.</summary>
    public static LoggingConfig Read(string settingsFilePath)
    {
        try
        {
            if (!File.Exists(settingsFilePath))
            {
                return new LoggingConfig();
            }

            var json = File.ReadAllText(settingsFilePath);
            return JsonSerializer.Deserialize(json, SettingsJsonContext.OnDisk.AppSettings)?.LoggingConfig
                   ?? new LoggingConfig();
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or JsonException
                                              or NotSupportedException)
        {
            // Defaults rather than a diagnostic: the file is SettingsStore's to complain about, and
            // it does so a moment later with a logger that by then exists.
            return new LoggingConfig();
        }
    }
}
