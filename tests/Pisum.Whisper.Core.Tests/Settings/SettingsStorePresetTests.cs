namespace Pisum.Whisper.Core.Tests.Settings;

using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>The preset operations the settings window will drive, exercised against a real file.</summary>
[IntegrationTest]
public sealed class SettingsStorePresetTests : IDisposable
{
    private readonly string _directory = string.Empty;

    private readonly string _path = string.Empty;

    public SettingsStorePresetTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, ".pisum-whisper.json");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    private SettingsStore NewStore()
    {
        return new SettingsStore(NullLogger<SettingsStore>.Instance, _path);
    }

    private SettingsStore LoadedStore()
    {
        var store = NewStore();
        store.Load();
        return store;
    }

    private static Preset Custom(string id, string name = "Custom", string prompt = "Say it plainly.")
    {
        return new Preset {Id = id, Name = name, SystemPrompt = prompt};
    }

    [Fact]
    public void SavePreset_WithAnUnknownId_AppendsIt()
    {
        var store = LoadedStore();

        store.SavePreset(Custom("mine"));

        store.Current.Presets.Select(p => p.Id).ShouldBe(["de-transcribe", "en-transcribe", "mine"]);
        NewStore().Load().Presets.ShouldContain(p => p.Id == "mine" && p.Name == "Custom");
    }

    [Fact]
    public void SavePreset_WithAKnownId_UpdatesTheNameAndPrompt()
    {
        var store = LoadedStore();
        store.SavePreset(Custom("mine"));

        store.SavePreset(Custom("mine", "Renamed", "Be terse."));

        var preset = store.Current.Presets.Single(p => p.Id == "mine");
        preset.Name.ShouldBe("Renamed");
        preset.SystemPrompt.ShouldBe("Be terse.");
        store.Current.Presets.Count(p => p.Id == "mine").ShouldBe(1);
    }

    [Fact]
    public void SavePreset_OverABuiltin_KeepsItBuiltinAndTheEditSurvivesTheNextLoad()
    {
        var store = LoadedStore();

        // isBuiltin is not the caller's to change, so passing false must not clear the flag.
        store.SavePreset(new Preset
        {
            Id = "de-transcribe",
            Name = "Diktat",
            SystemPrompt = "My own wording.",
            IsBuiltin = false,
        });

        var reloaded = NewStore().Load().Presets.Single(p => p.Id == "de-transcribe");
        reloaded.IsBuiltin.ShouldBeTrue();
        reloaded.Name.ShouldBe("Diktat");
        reloaded.SystemPrompt.ShouldBe("My own wording.");
    }

    [Fact]
    public void DeletePreset_RefusesABuiltinAndLeavesTheListUnchanged()
    {
        var store = LoadedStore();
        var before = store.Current.Presets.Select(p => p.Id).ToList();

        var exception = Should.Throw<SettingsException>(() => store.DeletePreset("de-transcribe"));

        exception.Message.ShouldContain("de-transcribe");
        store.Current.Presets.Select(p => p.Id).ShouldBe(before);
        NewStore().Load().Presets.Select(p => p.Id).ShouldBe(before);
    }

    [Fact]
    public void DeletePreset_RefusesAnUnknownId()
    {
        var store = LoadedStore();

        Should.Throw<SettingsException>(() => store.DeletePreset("nothing"))
            .Message.ShouldContain("nothing");
    }

    [Fact]
    public void DeletePreset_MovesTheActivePresetToTheFirstRemainingOne()
    {
        var store = LoadedStore();
        store.SavePreset(Custom("mine"));
        store.SetActivePreset("mine");

        store.DeletePreset("mine");

        // The first remaining preset, which is not necessarily the first built-in that Load falls
        // back to — the two rules differ on purpose.
        store.Current.ActivePresetId.ShouldBe("de-transcribe");
        NewStore().Load().ActivePresetId.ShouldBe("de-transcribe");
    }

    [Fact]
    public void DeletePreset_LeavesTheActivePresetAloneWhenAnotherIsDeleted()
    {
        var store = LoadedStore();
        store.SavePreset(Custom("mine"));
        store.SetActivePreset("en-transcribe");

        store.DeletePreset("mine");

        store.Current.ActivePresetId.ShouldBe("en-transcribe");
    }

    [Fact]
    public void SetActivePreset_PersistsAnExistingId()
    {
        var store = LoadedStore();

        store.SetActivePreset("en-transcribe");

        store.Current.ActivePresetId.ShouldBe("en-transcribe");
        NewStore().Load().ActivePresetId.ShouldBe("en-transcribe");
    }

    [Fact]
    public void SetActivePreset_RejectsAnUnknownIdAndKeepsThePreviousOne()
    {
        var store = LoadedStore();
        store.SetActivePreset("en-transcribe");

        Should.Throw<SettingsException>(() => store.SetActivePreset("nothing"))
            .Message.ShouldContain("nothing");

        store.Current.ActivePresetId.ShouldBe("en-transcribe");
        NewStore().Load().ActivePresetId.ShouldBe("en-transcribe");
    }
}
