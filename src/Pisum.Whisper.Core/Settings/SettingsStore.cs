using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// The single owner of application settings: loads them once, repairs what the reference found
/// worth repairing, serves every later read from memory, and persists mutations atomically.
/// </summary>
/// <remarks>
/// The store is cache-authoritative. It reads the file once and is the truth thereafter, so an edit
/// made to the file by hand while the application is running is lost at the next save. The upgrade
/// path is a debounced file watcher, which needs no change to this API.
/// </remarks>
public sealed class SettingsStore
{
    private const string FileName = ".pisum-whisper.json";

    private readonly ILogger<SettingsStore> _logger;

    public SettingsStore(ILogger<SettingsStore> logger)
        : this(logger, DefaultFilePath())
    {
    }

    /// <summary>Constructs a store over an explicit file, which is how the tests avoid the real home directory.</summary>
    public SettingsStore(ILogger<SettingsStore> logger, string filePath)
    {
        _logger = logger;
        FilePath = filePath;
    }

    /// <summary>Raised after settings are persisted, so components can re-apply without a restart.</summary>
    public event EventHandler<AppSettings>? Changed;

    public string FilePath { get; }

    /// <summary>Whether <see cref="Load"/> found no settings file and wrote the defaults itself.</summary>
    public bool IsFirstLaunch { get; private set; }

    /// <summary>The settings as they currently stand. Defaults until <see cref="Load"/> has run.</summary>
    public AppSettings Current { get; private set; } = new();

    public static string DefaultFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), FileName);

    /// <summary>
    /// Reads the settings file, creating it with defaults if it is absent, and repairs the two
    /// states the reference found worth fixing.
    /// </summary>
    /// <exception cref="SettingsException">The file exists but cannot be read or parsed.</exception>
    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            IsFirstLaunch = true;
            Current = new AppSettings();
            Write(Current);
            return Current;
        }

        IsFirstLaunch = false;
        var settings = Read();

        // The merge runs on every load, not only when a new built-in appears: it is also what makes
        // a file that omits `presets` entirely come back with the built-ins present.
        foreach (var builtin in BuiltinPresets.Create())
        {
            if (!settings.Presets.Any(preset => preset.Id == builtin.Id))
            {
                settings.Presets.Add(builtin);
            }
        }

        if (!settings.Presets.Any(preset => preset.Id == settings.ActivePresetId))
        {
            // Logged rather than repaired silently: a dangling id may be the symptom of a defect
            // elsewhere, and a silent fix would hide it.
            _logger.LogWarning(
                "Active preset '{InvalidId}' matches no preset; falling back to '{FallbackId}'.",
                settings.ActivePresetId,
                BuiltinPresets.DefaultId);

            settings.ActivePresetId = BuiltinPresets.DefaultId;

            // Written back, or the repair repeats on every launch over a file that still looks broken.
            Write(settings);
        }

        Current = settings;
        return Current;
    }

    /// <summary>Persists <paramref name="settings"/>, adopts them as the cache, and notifies subscribers.</summary>
    public void Save(AppSettings settings)
    {
        Write(settings);
        Current = settings;
        Changed?.Invoke(this, settings);
    }

    /// <summary>
    /// Adds <paramref name="preset"/> if its id is unknown, otherwise updates the existing preset's
    /// name and system prompt. Whether a preset is built-in is not the caller's to change, so a
    /// built-in stays built-in and keeps its edit through the next load's merge.
    /// </summary>
    public void SavePreset(Preset preset)
    {
        var existing = Current.Presets.FirstOrDefault(candidate => candidate.Id == preset.Id);
        if (existing is null)
        {
            Current.Presets.Add(preset);
        }
        else
        {
            existing.Name = preset.Name;
            existing.SystemPrompt = preset.SystemPrompt;
        }

        Save(Current);
    }

    /// <summary>Deletes a user preset, moving the active preset off it if it was the active one.</summary>
    /// <exception cref="SettingsException">No preset has this id, or the preset is built-in.</exception>
    public void DeletePreset(string id)
    {
        var preset = Current.Presets.FirstOrDefault(candidate => candidate.Id == id)
            ?? throw new SettingsException($"No preset with id '{id}' exists.");

        if (preset.IsBuiltin)
        {
            throw new SettingsException($"The built-in preset '{id}' cannot be deleted.");
        }

        Current.Presets.Remove(preset);

        // Deliberately the first remaining preset, not the first built-in that Load falls back to:
        // deleting the active preset should land on its neighbour, not jump back to the default.
        if (Current.ActivePresetId == id && Current.Presets.Count > 0)
        {
            Current.ActivePresetId = Current.Presets[0].Id;
        }

        Save(Current);
    }

    /// <summary>Switches the active preset.</summary>
    /// <exception cref="SettingsException">No preset has this id; the active preset is left alone.</exception>
    public void SetActivePreset(string id)
    {
        if (!Current.Presets.Any(preset => preset.Id == id))
        {
            throw new SettingsException($"No preset with id '{id}' exists.");
        }

        Current.ActivePresetId = id;
        Save(Current);
    }

    private AppSettings Read()
    {
        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (IOException exception)
        {
            throw new SettingsException(
                $"The settings file '{FilePath}' could not be read: {exception.Message}", exception);
        }

        try
        {
            return JsonSerializer.Deserialize(json, SettingsJsonContext.OnDisk.AppSettings) ?? new AppSettings();
        }
        catch (JsonException exception)
        {
            // Surfaced rather than overwritten with defaults: the file holds the user's API keys.
            throw new SettingsException(
                $"The settings file '{FilePath}' could not be parsed: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Writes via a temporary file in the same directory and moves it over the target. The reference
    /// writes in place, so an interruption mid-write truncates the file and the next launch refuses
    /// to start on a file the application itself corrupted.
    /// </summary>
    private void Write(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.OnDisk.AppSettings);
        var temporaryPath = FilePath + ".tmp";

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, FilePath, overwrite: true);
    }
}
