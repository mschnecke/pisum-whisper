namespace Pisum.Whisper.Core.Tests.Settings;

using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>
/// Task 1.8 — that the three preset operations clone, mutate and save rather than modifying the
/// published graph.
/// </summary>
/// <remarks>
/// The reason is a reader on another thread: the transcription path resolves the active preset's
/// prompt by scanning <c>Current.Presets</c>, and a list changed during that scan throws rather than
/// returning a stale answer, losing a dictation the user has already spoken. Replacing the graph
/// once per operation makes every read see the old settings in full or the new settings in full.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class SettingsStorePresetRaceTests : IDisposable
{
    private readonly string _directory;

    private readonly string _path;

    public SettingsStorePresetRaceTests()
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

    private static Preset Custom(string id, string name = "Custom", string prompt = "Say it plainly.")
    {
        return new Preset {Id = id, Name = name, SystemPrompt = prompt};
    }

    [Fact]
    public void SavePreset_ReplacesCurrentAndLeavesTheCapturedGraphAlone()
    {
        var store = LoadedStore();
        var before = store.Current;
        var capturedPresets = before.Presets;
        var capturedIds = capturedPresets.Select(preset => preset.Id).ToList();

        store.SavePreset(Custom("mine"));

        store.Current.ShouldNotBeSameAs(before);
        before.Presets.ShouldBeSameAs(capturedPresets);
        capturedPresets.Select(preset => preset.Id).ShouldBe(capturedIds);
        store.Current.Presets.Select(preset => preset.Id).ShouldBe([.. capturedIds, "mine"]);
    }

    [Fact]
    public void SavePreset_UpdatingOne_ReplacesCurrentAndLeavesTheCapturedPresetAlone()
    {
        var store = LoadedStore();
        store.SavePreset(Custom("mine"));
        var before = store.Current;
        var capturedPreset = before.Presets.Single(preset => preset.Id == "mine");

        store.SavePreset(Custom("mine", "Renamed", "Be terse."));

        store.Current.ShouldNotBeSameAs(before);
        capturedPreset.Name.ShouldBe("Custom");
        capturedPreset.SystemPrompt.ShouldBe("Say it plainly.");
        store.Current.Presets.Single(preset => preset.Id == "mine").Name.ShouldBe("Renamed");
    }

    [Fact]
    public void SavePreset_AddsACopyRatherThanTheCallersInstance()
    {
        var store = LoadedStore();
        var mine = Custom("mine");

        store.SavePreset(mine);

        var stored = store.Current.Presets.Single(preset => preset.Id == "mine");
        stored.ShouldNotBeSameAs(mine);

        // The caller keeping its instance and editing it must not reach into the published graph.
        mine.Name = "Edited behind the store's back";
        stored.Name.ShouldBe("Custom");
    }

    [Fact]
    public void DeletePreset_ReplacesCurrentAndLeavesTheCapturedGraphAlone()
    {
        var store = LoadedStore();
        store.SavePreset(Custom("mine"));
        var before = store.Current;
        var capturedIds = before.Presets.Select(preset => preset.Id).ToList();

        store.DeletePreset("mine");

        store.Current.ShouldNotBeSameAs(before);
        before.Presets.Select(preset => preset.Id).ShouldBe(capturedIds);
        store.Current.Presets.Select(preset => preset.Id).ShouldNotContain("mine");
    }

    [Fact]
    public void DeletePreset_OfTheActiveOne_NeverPublishesADanglingActiveId()
    {
        var store = LoadedStore();
        store.SavePreset(Custom("mine"));
        store.SetActivePreset("mine");
        var before = store.Current;

        store.DeletePreset("mine");

        // The old graph still names a preset it still holds; the new one names a preset it holds.
        // There is no third state in between, because Current moves in one assignment.
        before.Presets.ShouldContain(preset => preset.Id == before.ActivePresetId);
        store.Current.Presets.ShouldContain(preset => preset.Id == store.Current.ActivePresetId);
    }

    [Fact]
    public void SetActivePreset_ReplacesCurrentAndLeavesTheCapturedGraphAlone()
    {
        var store = LoadedStore();
        var before = store.Current;
        var activeBefore = before.ActivePresetId;

        store.SetActivePreset("de-transcribe");

        store.Current.ShouldNotBeSameAs(before);
        before.ActivePresetId.ShouldBe(activeBefore);
        store.Current.ActivePresetId.ShouldBe("de-transcribe");
    }

    [Fact]
    public void ARejectedDelete_LeavesCurrentReferentiallyIdenticalAndFiresNoChanged()
    {
        var store = LoadedStore();
        var before = store.Current;
        var changes = 0;
        store.Changed += (_, _) => changes++;

        Should.Throw<SettingsException>(() => store.DeletePreset("de-transcribe"));
        Should.Throw<SettingsException>(() => store.DeletePreset("nothing"));

        store.Current.ShouldBeSameAs(before);
        changes.ShouldBe(0);
    }

    [Fact]
    public void ARejectedActivation_LeavesCurrentReferentiallyIdenticalAndFiresNoChanged()
    {
        var store = LoadedStore();
        var before = store.Current;
        var changes = 0;
        store.Changed += (_, _) => changes++;

        Should.Throw<SettingsException>(() => store.SetActivePreset("nothing"));

        store.Current.ShouldBeSameAs(before);
        changes.ShouldBe(0);
    }

    [Fact]
    public async Task APresetIsDeletedWhileTheActivePromptIsBeingResolvedRepeatedly()
    {
        // The failure this closes, reproduced: the transcription path scans Current.Presets on a
        // pooled thread while the settings window deletes one. Against the previous in-place
        // mutation this threw InvalidOperationException from the enumeration, or from First finding
        // nothing in the window between the removal and the reassignment.
        //
        // The presets are seeded in one Save rather than one per SavePreset. Each write is a real
        // File.Move over the same path, and a few hundred of them back to back is how this test
        // fails on a UnauthorizedAccessException that has nothing to do with what it is measuring.
        // What has to be repeated is the *read*, and the reader below runs for the whole loop.
        const int PresetCount = 30;

        var store = LoadedStore();
        var seeded = store.CloneCurrent();
        for (var index = 0; index < PresetCount; index++)
        {
            seeded.Presets.Add(Custom($"mine-{index}"));
        }

        seeded.ActivePresetId = "mine-0";
        store.Save(seeded);

        var reads = 0;
        var reading = new TaskCompletionSource();
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var settings = store.Current;
                _ = settings.Presets.First(preset => preset.Id == settings.ActivePresetId).SystemPrompt;
                reads++;
                reading.TrySetResult();
            }
        }, TestContext.Current.CancellationToken);

        // The deletions must overlap the reader, and Task.Run only promises to queue it. On a
        // starved pool the thirty writes below can run to completion before it is scheduled at all,
        // which leaves reads at 0 and fails the guard at the end for a reason that has nothing to do
        // with what this measures. Waiting for its first iteration makes the overlap a fact rather
        // than a hope; the bound is here so a reader that never runs still fails rather than hangs.
        //
        // The finally is what makes that bound honest. The reader's loop is tight and leaves only on
        // the token, and disposing a CancellationTokenSource does not cancel it — so a wait that
        // timed out would throw past the cancel and leave that loop spinning on a pool thread for
        // the rest of the run, which is the hang this bound exists to avoid.
        try
        {
            await reading.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            for (var index = 0; index < PresetCount; index++)
            {
                store.DeletePreset($"mine-{index}");
            }
        }
        finally
        {
            await stop.CancelAsync();
        }

        await Should.NotThrowAsync(reader);

        // So a reader that never actually ran cannot pass this test by doing nothing.
        reads.ShouldBeGreaterThan(0);
    }
}
