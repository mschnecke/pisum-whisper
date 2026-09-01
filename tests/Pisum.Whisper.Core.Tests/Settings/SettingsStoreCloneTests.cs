namespace Pisum.Whisper.Core.Tests.Settings;

using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>
/// Task 1.7 — <see cref="SettingsStore.CloneCurrent"/>, the deep copy every runtime write is built
/// on. It is a copy in every nested collection, or a draft would still be able to reach into the
/// graph a reader is holding.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class SettingsStoreCloneTests : IDisposable
{
    private readonly string _directory;

    private readonly string _path;

    public SettingsStoreCloneTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, ".pisum-whisper.json");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    private SettingsStore LoadedStore()
    {
        var store = new SettingsStore(NullLogger<SettingsStore>.Instance, _path);
        store.Load();
        return store;
    }

    [Fact]
    public void TheCloneEqualsTheOriginalFieldForField()
    {
        var store = LoadedStore();
        store.Save(new AppSettings
        {
            StartWithSystem = false,
            ShowTrayNotifications = false,
            Hotkey = new HotkeyBinding {Modifiers = ["Alt", "Shift"], Key = "F9"},
            AudioFormat = AudioFormat.Wav,
            ActivePresetId = "en-transcribe",
            Providers = [new ProviderConfig {Id = "one", ApiKey = "key-one", Model = "gemini-2.5-flash"}],
            RecordingMode = RecordingMode.Toggle,
            MaxRecordingDurationSecs = 42,
            LoggingConfig = new LoggingConfig {LogLevel = "debug", LogMaxFileSizeMb = 5, LogRetentionDays = 3},
        });

        var clone = store.CloneCurrent();

        clone.StartWithSystem.ShouldBeFalse();
        clone.ShowTrayNotifications.ShouldBeFalse();
        clone.Hotkey.Modifiers.ShouldBe(["Alt", "Shift"]);
        clone.Hotkey.Key.ShouldBe("F9");
        clone.AudioFormat.ShouldBe(AudioFormat.Wav);
        clone.ActivePresetId.ShouldBe("en-transcribe");
        clone.RecordingMode.ShouldBe(RecordingMode.Toggle);
        clone.MaxRecordingDurationSecs.ShouldBe(42);
        clone.LoggingConfig.LogLevel.ShouldBe("debug");
        clone.LoggingConfig.LogMaxFileSizeMb.ShouldBe(5);
        clone.LoggingConfig.LogRetentionDays.ShouldBe(3);

        var provider = clone.Providers.Single();
        provider.Id.ShouldBe("one");
        provider.ApiKey.ShouldBe("key-one");
        provider.Model.ShouldBe("gemini-2.5-flash");
        provider.Enabled.ShouldBeTrue();

        clone.Presets.Select(preset => preset.Id).ShouldBe(store.Current.Presets.Select(preset => preset.Id));
    }

    [Fact]
    public void TheCloneIsADifferentGraphAllTheWayDown()
    {
        var store = LoadedStore();
        store.Save(new AppSettings
        {
            Providers = [new ProviderConfig {Id = "one", ApiKey = "key-one"}],
        });

        var clone = store.CloneCurrent();

        clone.ShouldNotBeSameAs(store.Current);
        clone.Presets.ShouldNotBeSameAs(store.Current.Presets);
        clone.Presets[0].ShouldNotBeSameAs(store.Current.Presets[0]);
        clone.Providers.ShouldNotBeSameAs(store.Current.Providers);
        clone.Providers[0].ShouldNotBeSameAs(store.Current.Providers[0]);
        clone.Hotkey.ShouldNotBeSameAs(store.Current.Hotkey);
        clone.Hotkey.Modifiers.ShouldNotBeSameAs(store.Current.Hotkey.Modifiers);
        clone.LoggingConfig.ShouldNotBeSameAs(store.Current.LoggingConfig);
    }

    [Fact]
    public void MutatingTheCloneLeavesCurrentUntouched()
    {
        var store = LoadedStore();
        store.Save(new AppSettings
        {
            Providers = [new ProviderConfig {Id = "one", ApiKey = "key-one"}],
        });

        var clone = store.CloneCurrent();
        clone.AudioFormat = AudioFormat.Wav;
        clone.MaxRecordingDurationSecs = 7;
        clone.Hotkey.Key = "F12";
        clone.Hotkey.Modifiers.Add("Alt");
        clone.LoggingConfig.LogLevel = "trace";
        clone.Providers[0].ApiKey = "replaced";
        clone.Providers.Add(new ProviderConfig {Id = "two", ApiKey = "key-two"});
        clone.Presets[0].SystemPrompt = "replaced";
        clone.Presets.RemoveAt(0);

        store.Current.AudioFormat.ShouldBe(AudioFormat.Opus);
        store.Current.MaxRecordingDurationSecs.ShouldBe(600);
        store.Current.Hotkey.Key.ShouldBe(new HotkeyBinding().Key);
        store.Current.Hotkey.Modifiers.ShouldBe(new HotkeyBinding().Modifiers);
        store.Current.LoggingConfig.LogLevel.ShouldBe("info");
        store.Current.Providers.Count.ShouldBe(1);
        store.Current.Providers[0].ApiKey.ShouldBe("key-one");
        store.Current.Presets.Count.ShouldBe(2);
        store.Current.Presets[0].SystemPrompt.ShouldNotBe("replaced");
    }

    [Fact]
    public void ANonAsciiPresetPromptSurvivesTheRoundTrip()
    {
        // The clone goes through the on-disk context, whose relaxed encoder is what keeps German
        // legible in the settings file. A clone that mangled it would mangle the next save with it.
        const string Prompt = "Schreibe flüssiges Deutsch – ohne Füllwörter, größtenteils.";

        var store = LoadedStore();
        store.SavePreset(new Preset {Id = "mine", Name = "Diktat", SystemPrompt = Prompt});

        store.CloneCurrent().Presets.Single(preset => preset.Id == "mine").SystemPrompt.ShouldBe(Prompt);
    }
}
