namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// Represents the application settings for the Pisum Whisper application.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// The value indicating whether the application starts with the system.
    /// When enabled, the application will automatically launch during system startup.
    /// </summary>
    public bool StartWithSystem { get; init; } = true;

    /// <summary>
    /// Indicates whether notifications are displayed in the system tray.
    /// When enabled, the application can show relevant alerts and messages in the tray area during operation.
    /// </summary>
    public bool ShowTrayNotifications { get; init; } = true;

    /// <summary>
    /// Represents the user's configured hotkey binding used to trigger specific application functionalities.
    /// This property allows customization of key combinations for optimal user experience.
    /// </summary>
    public HotkeyBinding Hotkey { get; init; } = new();

    /// <summary>
    /// Represents the audio format used for recording or playback in the application.
    /// Available formats include:
    /// - Opus: A lossy compressed format optimized for low-latency audio.
    /// - Wav: An uncompressed audio format that provides high fidelity.
    /// </summary>
    public AudioFormat AudioFormat { get; init; } = AudioFormat.Opus;

    /// <summary>
    /// A collection of configuration presets used to customize application behavior.
    /// Each preset defines a specific configuration for the system, including its identifier,
    /// name, system prompt, and whether it is built-in. This property provides the ability to
    /// load and manage multiple presets, including both built-in and custom ones.
    /// </summary>
    public List<Preset> Presets { get; init; } = BuiltinPresets.Create();

    /// <summary>
    /// The identifier of the currently active preset in the application settings.
    /// This value determines which preset configuration is applied for the application's operations.
    /// Defaults to the built-in preset identified by <c>BuiltinPresets.DefaultId</c>.
    /// </summary>
    public string ActivePresetId { get; set; } = BuiltinPresets.DefaultId;

    /// <summary>
    /// Represents a collection of provider configurations used within the application.
    /// The <c>Providers</c> property stores a list of <see cref="ProviderConfig"/> objects. Each entry in the list
    /// represents configuration details for a specific provider, such as API keys, model details, or other relevant settings.
    /// This allows the application to support multiple providers, enabling flexible and extensible integration.
    /// </summary>
    public List<ProviderConfig> Providers { get; init; } = [];

    /// <summary>
    /// Specifies the mode of operation for recording.
    /// The RecordingMode property determines how the recording functionality is activated
    /// and controlled within the application. The available options are:
    /// - HoldToRecord: Recording is active only while a designated control is held down.
    /// - Toggle: Recording is started and stopped with a single toggle action.
    /// </summary>
    public RecordingMode RecordingMode { get; init; } = RecordingMode.HoldToRecord;

    /// <summary>
    /// Specifies the maximum allowable duration for a recording session, in seconds.
    /// The default value is 600 seconds (10 minutes). This value can be customized
    /// based on application requirements or user settings. Reducing this value
    /// could be useful for managing storage or enforcing shorter recording limits,
    /// while increasing it may allow for extended recordings.
    /// </summary>
    public int MaxRecordingDurationSecs { get; init; } = 600;

    /// <summary>
    /// Represents configuration settings for logging functionality.
    /// This class manages parameters for controlling logging behavior, including
    /// the logging level, maximum log file size, and log retention duration.
    /// These settings are utilized by the logging system to determine how logs
    /// are written and maintained.
    /// </summary>
    public LoggingConfig LoggingConfig { get; init; } = new();
}
