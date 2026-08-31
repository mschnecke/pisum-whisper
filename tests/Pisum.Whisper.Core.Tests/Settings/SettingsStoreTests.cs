namespace Pisum.Whisper.Core.Tests.Settings;

using System.Text.Json;
using FakeItEasy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Settings;
using Shouldly;

[IntegrationTest]
public sealed class SettingsStoreTests : IDisposable
{
    private string _directory = string.Empty;

    private string _path = string.Empty;

    public SettingsStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, ".pisum-whisper.json");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    private SettingsStore NewStore(ILogger<SettingsStore>? logger = null)
    {
        return new SettingsStore(logger ?? NullLogger<SettingsStore>.Instance, _path);
    }

    private void WriteFile(string json)
    {
        File.WriteAllText(_path, json);
    }

    [Fact]
    public void DefaultFilePath_IsTheDotFileInTheHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        SettingsStore.DefaultFilePath().ShouldBe(Path.Combine(home, ".pisum-whisper.json"));
    }

    [Fact]
    public void Load_ReadsAHandWrittenFile()
    {
        WriteFile(
            """
            {
              "startWithSystem": false,
              "hotkey": { "modifiers": ["Alt"], "key": "F9" },
              "audioFormat": "wav",
              "activePresetId": "en-transcribe",
              "providers": [{ "id": "gemini", "apiKey": "abc123", "model": "gemini-2.5-flash" }],
              "recordingMode": "toggle",
              "maxRecordingDurationSecs": 30,
              "loggingConfig": { "logLevel": "debug", "logMaxFileSizeMb": 5, "logRetentionDays": 2 }
            }
            """);

        var settings = NewStore().Load();

        settings.StartWithSystem.ShouldBeFalse();
        settings.Hotkey.Modifiers.ShouldBe(["Alt"]);
        settings.Hotkey.Key.ShouldBe("F9");
        settings.AudioFormat.ShouldBe(AudioFormat.Wav);
        settings.ActivePresetId.ShouldBe("en-transcribe");
        settings.Providers.Single().ApiKey.ShouldBe("abc123");
        settings.Providers.Single().Enabled.ShouldBeTrue();
        settings.RecordingMode.ShouldBe(RecordingMode.Toggle);
        settings.MaxRecordingDurationSecs.ShouldBe(30);
        settings.LoggingConfig.LogLevel.ShouldBe("debug");
        settings.LoggingConfig.LogMaxFileSizeMb.ShouldBe(5);
        settings.LoggingConfig.LogRetentionDays.ShouldBe(2);
    }

    [Fact]
    public void Load_WithNoFile_WritesDefaultsAndReportsAFirstLaunch()
    {
        var store = NewStore();

        var settings = store.Load();

        store.IsFirstLaunch.ShouldBeTrue();
        File.Exists(_path).ShouldBeTrue();
        settings.ShouldBeSameAs(store.Current);
        File.ReadAllText(_path).ShouldContain("\"activePresetId\": \"en-transcribe\"");

        // The flag reports this run, not the history of the file: a second store over the same file
        // sees an ordinary launch.
        var second = NewStore();
        second.Load();
        second.IsFirstLaunch.ShouldBeFalse();
    }

    [Fact]
    public void Load_WithAPartialFile_DefaultsEveryOtherProperty()
    {
        WriteFile("""{"startWithSystem": false}""");

        var settings = NewStore().Load();
        var defaults = new AppSettings();

        settings.StartWithSystem.ShouldBeFalse();
        settings.ShowTrayNotifications.ShouldBe(defaults.ShowTrayNotifications);
        settings.Hotkey.Modifiers.ShouldBe(defaults.Hotkey.Modifiers);
        settings.Hotkey.Key.ShouldBe(defaults.Hotkey.Key);
        settings.AudioFormat.ShouldBe(defaults.AudioFormat);
        settings.Presets.Select(p => p.Id).ShouldBe(defaults.Presets.Select(p => p.Id));
        settings.ActivePresetId.ShouldBe(defaults.ActivePresetId);
        settings.Providers.ShouldBeEmpty();
        settings.RecordingMode.ShouldBe(defaults.RecordingMode);
        settings.MaxRecordingDurationSecs.ShouldBe(defaults.MaxRecordingDurationSecs);
        settings.LoggingConfig.LogLevel.ShouldBe(defaults.LoggingConfig.LogLevel);
        settings.LoggingConfig.LogMaxFileSizeMb.ShouldBe(defaults.LoggingConfig.LogMaxFileSizeMb);
        settings.LoggingConfig.LogRetentionDays.ShouldBe(defaults.LoggingConfig.LogRetentionDays);
    }

    [Fact]
    public void Load_AddsMissingBuiltinPresets()
    {
        WriteFile("""{"presets": [], "activePresetId": "de-transcribe"}""");

        var settings = NewStore().Load();

        settings.Presets.Select(p => p.Id).ShouldBe(["de-transcribe", "en-transcribe"]);
        settings.Presets.ShouldAllBe(p => p.IsBuiltin);
    }

    [Fact]
    public void Load_KeepsAnEditedBuiltinPreset()
    {
        WriteFile(
            """
            {
              "presets": [
                { "id": "de-transcribe", "name": "Mine", "systemPrompt": "My own wording.", "isBuiltin": true }
              ]
            }
            """);

        var settings = NewStore().Load();

        var edited = settings.Presets.Single(p => p.Id == "de-transcribe");
        edited.Name.ShouldBe("Mine");
        edited.SystemPrompt.ShouldBe("My own wording.");
        settings.Presets.ShouldContain(p => p.Id == "en-transcribe");
    }

    [Fact]
    public void Load_WithADanglingActivePresetId_RepairsRewritesAndWarns()
    {
        WriteFile("""{"activePresetId": "gone"}""");
        var logger = A.Fake<ILogger<SettingsStore>>();

        var settings = NewStore(logger).Load();

        settings.ActivePresetId.ShouldBe("en-transcribe");
        NewStore().Load().ActivePresetId.ShouldBe("en-transcribe");
        File.ReadAllText(_path).ShouldContain("\"activePresetId\": \"en-transcribe\"");

        // The repair is logged rather than silent, so a dangling id caused by a defect elsewhere
        // still leaves a trace.
        A.CallTo(logger)
            .Where(call => call.Method.Name == nameof(ILogger.Log)
                           && call.GetArgument<LogLevel>(0) == LogLevel.Warning
                           && call.Arguments[2]!.ToString()!.Contains("gone"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Save_UpdatesTheCacheAndNotifiesSubscribers()
    {
        var store = NewStore();
        store.Load();

        AppSettings? notified = null;
        store.Changed += (_, settings) => notified = settings;

        store.Save(new AppSettings {MaxRecordingDurationSecs = 42, StartWithSystem = false});

        notified.ShouldNotBeNull();
        notified.MaxRecordingDurationSecs.ShouldBe(42);
        store.Current.MaxRecordingDurationSecs.ShouldBe(42);
        NewStore().Load().StartWithSystem.ShouldBeFalse();
    }

    [Fact]
    public void Load_WithInvalidJson_ThrowsNamingThePathAndLeavesTheFileUntouched()
    {
        WriteFile("""{"startWithSystem": tru""");
        var before = File.ReadAllBytes(_path);

        var exception = Should.Throw<SettingsException>(() => NewStore().Load());

        exception.Message.ShouldContain(_path);
        exception.InnerException.ShouldBeOfType<JsonException>();
        File.ReadAllBytes(_path).ShouldBe(before);
    }

    [Fact]
    public void Load_WithAPresetMissingItsName_ThrowsRatherThanMaterialisingNulls()
    {
        WriteFile("""{"presets":[{"id":"x"}]}""");

        Should.Throw<SettingsException>(() => NewStore().Load()).Message.ShouldContain(_path);
    }

    [Fact]
    public void Load_WithAProviderMissingItsApiKey_Throws()
    {
        WriteFile("""{"providers":[{"id":"gemini"}]}""");

        Should.Throw<SettingsException>(() => NewStore().Load()).Message.ShouldContain(_path);
    }

    [Fact]
    public void Save_ReplacesTheFileRatherThanAppendingToIt()
    {
        var store = NewStore();
        store.Load();

        // Settings carrying a long custom preset, then settings without it: a write in place would
        // leave the tail of the longer document behind.
        var longer = new AppSettings();
        longer.Presets.Add(new Preset {Id = "long", Name = "Long", SystemPrompt = new string('x', 4000)});
        store.Save(longer);
        var longLength = new FileInfo(_path).Length;

        store.Save(new AppSettings());

        new FileInfo(_path).Length.ShouldBeLessThan(longLength);
        File.ReadAllText(_path).ShouldNotContain("xxxx");
        File.ReadAllText(_path).TrimEnd().ShouldEndWith("}");
        NewStore().Load().Presets.Select(p => p.Id).ShouldBe(["de-transcribe", "en-transcribe"]);
    }

    [Fact]
    public void Save_WritesThroughATemporaryFileAndLeavesNothingBehind()
    {
        var store = NewStore();
        store.Load();

        // A stale temporary from an interrupted save must not block the next one.
        File.WriteAllText(_path + ".tmp", "leftover");

        store.Save(new AppSettings {StartWithSystem = false});

        // Only the target remains, so the new content reached it by a move rather than by being
        // written into it — a partially written document is never observable at the settings path.
        Directory.GetFiles(_directory).ShouldBe([_path]);
        NewStore().Load().StartWithSystem.ShouldBeFalse();
    }
}
