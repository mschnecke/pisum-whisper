namespace Pisum.Whisper.Core.Diagnostics;

using Pisum.Whisper.Core.Settings;

internal static class StartupFailure
{
    internal const string SettingsErrorTitle = "Settings Error";

    internal const string StartupErrorTitle = "Startup Error";

    internal const string StartupErrorMessage = "Pisum Whisper could not start.";

     public static (string Title, string Message) Describe(Exception exception, string? logFilePath)
    {
        var (title, message) = exception switch
        {
            // Its message already names the file and says what is wrong with it, which is the whole
            // of what makes the file repairable by hand.
            SettingsException => (SettingsErrorTitle, exception.Message),

            _ => (StartupErrorTitle, StartupErrorMessage),
        };

        return (title, WithLogPointer(message, logFilePath));
    }

    private static string WithLogPointer(string message, string? logFilePath)
    {
        return logFilePath is null
            ? message
            : $"{message}{Environment.NewLine}{Environment.NewLine}"
              + $"The log for this launch would be written to {logFilePath}.";
    }
}
