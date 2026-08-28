namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// A settings operation the store refuses to perform: an unreadable or malformed file, or a preset
/// request that would leave the settings inconsistent. One type mirrors the reference's single
/// configuration error, and its message is written to be shown to the user as-is.
/// </summary>
public sealed class SettingsException(string message, Exception? innerException = null)
    : Exception(message, innerException);
