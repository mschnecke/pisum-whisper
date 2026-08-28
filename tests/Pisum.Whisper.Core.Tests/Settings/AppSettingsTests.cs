namespace Pisum.Whisper.Core.Tests.Settings;

using System.Text.Json;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>
/// The settings shape is fixed by this change because five later changes read it. These tests pin
/// the schema table in <c>specs/settings-persistence/spec.md</c> so a drift is a test failure.
/// </summary>
[TestClass]
public sealed class AppSettingsTests
{
    private static string Serialize(AppSettings settings)
    {
        return JsonSerializer.Serialize(settings, SettingsJsonContext.OnDisk.AppSettings);
    }

    [TestMethod]
    public void Defaults_MatchTheSchemaTable()
    {
        var settings = new AppSettings();

        settings.StartWithSystem.ShouldBeTrue();
        settings.ShowTrayNotifications.ShouldBeTrue();
        settings.Hotkey.Modifiers.ShouldBe(OperatingSystem.IsMacOS() ? ["Cmd", "Shift"] : ["Ctrl", "Shift"]);
        settings.Hotkey.Key.ShouldBe("Space");
        settings.AudioFormat.ShouldBe(AudioFormat.Opus);
        settings.Presets.Select(p => p.Id).ShouldBe(["de-transcribe", "en-transcribe"]);
        settings.ActivePresetId.ShouldBe("en-transcribe");
        settings.Providers.ShouldBeEmpty();
        settings.RecordingMode.ShouldBe(RecordingMode.HoldToRecord);
        settings.MaxRecordingDurationSecs.ShouldBe(600);
        settings.LoggingConfig.LogLevel.ShouldBe("info");
        settings.LoggingConfig.LogMaxFileSizeMb.ShouldBe(1);
        settings.LoggingConfig.LogRetentionDays.ShouldBe(7);
    }

    [TestMethod]
    public void Preset_RoundTripsThroughJson()
    {
        var preset = new Preset
        {
            Id = "custom",
            Name = "Custom",
            SystemPrompt = "Say it plainly.",
            IsBuiltin = true,
        };

        var json = JsonSerializer.Serialize(preset, SettingsJsonContext.OnDisk.Preset);
        var restored = JsonSerializer.Deserialize(json, SettingsJsonContext.OnDisk.Preset)!;

        restored.Id.ShouldBe(preset.Id);
        restored.Name.ShouldBe(preset.Name);
        restored.SystemPrompt.ShouldBe(preset.SystemPrompt);
        restored.IsBuiltin.ShouldBe(preset.IsBuiltin);
    }

    [TestMethod]
    public void ProviderConfig_RoundTripsThroughJson()
    {
        var provider = new ProviderConfig
        {
            Id = "gemini",
            ApiKey = "secret",
            Model = "gemini-2.5-flash",
            Enabled = false,
        };

        var json = JsonSerializer.Serialize(provider, SettingsJsonContext.OnDisk.ProviderConfig);
        var restored = JsonSerializer.Deserialize(json, SettingsJsonContext.OnDisk.ProviderConfig)!;

        restored.Id.ShouldBe(provider.Id);
        restored.ApiKey.ShouldBe(provider.ApiKey);
        restored.Model.ShouldBe(provider.Model);
        restored.Enabled.ShouldBe(provider.Enabled);
    }

    [TestMethod]
    public void ProviderConfig_DefaultsToEnabledWithNoModel()
    {
        var json = """{"id":"gemini","apiKey":"secret"}""";

        var provider = JsonSerializer.Deserialize(json, SettingsJsonContext.OnDisk.ProviderConfig)!;

        provider.Model.ShouldBeNull();
        provider.Enabled.ShouldBeTrue();
    }

    [TestMethod]
    public void SerializedPropertyNames_AreCamelCase()
    {
        var json = Serialize(new AppSettings());

        json.ShouldContain("\"startWithSystem\"");

        // Case-sensitively: Shouldly compares case-insensitively unless told otherwise.
        json.ShouldNotContain("\"StartWithSystem\"", Case.Sensitive);
        json.ShouldContain("\"maxRecordingDurationSecs\"");
        json.ShouldContain("\"systemPrompt\"");
        json.ShouldContain("\"isBuiltin\"");
    }

    [TestMethod]
    public void NonAsciiText_IsWrittenAsItselfRatherThanEscaped()
    {
        // The file is advertised as hand-editable and users write German prompts and preset names
        // into it. The default encoder would escape every umlaut, which nobody can edit by hand.
        // The built-in prompts are English, so this pins the encoder on user-authored text.
        var settings = new AppSettings();
        settings.Presets.Add(
            new Preset {Id = "de", Name = "Flüssig", SystemPrompt = "Schreibe flüssige Sätze."});

        var json = Serialize(settings);

        json.ShouldContain("flüssige");
        json.ShouldNotContain("u00fc");
    }

    [TestMethod]
    public void BuiltinPresets_AreBothPresentAndMarkedBuiltin()
    {
        var presets = BuiltinPresets.Create();

        presets.Select(p => p.Id).ShouldBe(["de-transcribe", "en-transcribe"]);
        presets.ShouldAllBe(p => p.IsBuiltin);
        presets.ShouldAllBe(p => p.SystemPrompt.Length > 0);
        presets.Select(p => p.Name).ShouldBe(["Transcribe DE", "Transcribe EN"]);
    }

    [TestMethod]
    public void BuiltinPresets_AreNotSharedBetweenCallers()
    {
        // Presets are mutable and get merged into the user's list, so a shared instance would let
        // one caller's edit leak into another's defaults.
        BuiltinPresets.Create()[0].ShouldNotBeSameAs(BuiltinPresets.Create()[0]);
    }

    [TestMethod]
    public void Enums_SerializeToTheDocumentedStrings()
    {
        Serialize(new AppSettings {AudioFormat = AudioFormat.Opus}).ShouldContain("\"audioFormat\": \"opus\"");
        Serialize(new AppSettings {AudioFormat = AudioFormat.Wav}).ShouldContain("\"audioFormat\": \"wav\"");
        Serialize(new AppSettings {RecordingMode = RecordingMode.HoldToRecord})
            .ShouldContain("\"recordingMode\": \"holdToRecord\"");
        Serialize(new AppSettings {RecordingMode = RecordingMode.Toggle})
            .ShouldContain("\"recordingMode\": \"toggle\"");
    }

    [TestMethod]
    public void DroppedReferenceFields_AreAbsent()
    {
        // The first two went with local inference; the third went with the decision that Gemini is
        // the only provider type.
        var json = Serialize(new AppSettings());

        json.ShouldNotContain("transcriptionMode");
        json.ShouldNotContain("whisperConfig");
        json.ShouldNotContain("providerType");
    }
}
