namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// The whole of the application's configuration, as it is stored in <c>~/.pisum-whisper.json</c>.
/// </summary>
/// <remarks>
/// Every property carries a default. That is what lets a file written by an older version — or a
/// hand-edited file missing half its properties — load without error, and it is why no schema
/// version or migration step is needed. Adding a property here must stay a non-event.
/// <para>
/// The collections are declared with setters on purpose: <c>System.Text.Json</c> replaces a
/// settable collection on deserialization but <em>appends</em> to a get-only one, which would
/// duplicate the built-in presets on every load.
/// </para>
/// </remarks>
public sealed class AppSettings
{
    public bool StartWithSystem { get; set; } = true;

    public bool ShowTrayNotifications { get; set; } = true;

    public HotkeyBinding Hotkey { get; set; } = new();

    public AudioFormat AudioFormat { get; set; } = AudioFormat.Opus;

    public List<Preset> Presets { get; set; } = BuiltinPresets.Create();

    public string ActivePresetId { get; set; } = BuiltinPresets.DefaultId;

    public List<ProviderConfig> Providers { get; set; } = [];

    public RecordingMode RecordingMode { get; set; } = RecordingMode.HoldToRecord;

    public int MaxRecordingDurationSecs { get; set; } = 600;

    public LoggingConfig LoggingConfig { get; set; } = new();
}
