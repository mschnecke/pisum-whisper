namespace Pisum.Whisper.App.Tests.Settings;

using Pisum.Whisper.App.Tests;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>
/// Tasks 2.1 to 2.5 — the debounced, clone-and-replace edit model the whole window writes through.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class SettingsEditorTests : SettingsEditorTestBase
{
    // ---- Task 2.1: an edit lands on a clone and is not written yet ----

    [Fact]
    public void TheFirstEdit_ClonesFromCurrentAndDoesNotSave()
    {
        var editor = NewEditor();
        var before = Store.Current;

        editor.Edit(settings => settings.AudioFormat = AudioFormat.Wav);

        Saves.ShouldBe(0);
        Store.Current.ShouldBeSameAs(before);
        Store.Current.AudioFormat.ShouldBe(AudioFormat.Opus);
    }

    [Fact]
    public async Task TheSaveLandsWhenTheQuietWindowCompletes()
    {
        var editor = NewEditor();
        editor.Edit(settings => settings.AudioFormat = AudioFormat.Wav);

        CompleteQuietWindow();

        await WaitForAsync(() => Saves == 1);
        Saves.ShouldBe(1);
        Store.Current.AudioFormat.ShouldBe(AudioFormat.Wav);
    }

    // ---- Task 2.2: continuous editing coalesces into one write ----

    [Fact]
    public async Task FiveEditsInOneQuietWindow_ProduceOneSaveCarryingAllFive()
    {
        var editor = NewEditor();

        editor.Edit(settings => settings.AudioFormat = AudioFormat.Wav);
        editor.Edit(settings => settings.RecordingMode = RecordingMode.Toggle);
        editor.Edit(settings => settings.MaxRecordingDurationSecs = 90);
        editor.Edit(settings => settings.LoggingConfig.LogLevel = "debug");
        editor.Edit(settings => settings.StartWithSystem = false);

        CompleteQuietWindow();
        await WaitForAsync(() => Saves == 1);

        Saves.ShouldBe(1);
        Store.Current.AudioFormat.ShouldBe(AudioFormat.Wav);
        Store.Current.RecordingMode.ShouldBe(RecordingMode.Toggle);
        Store.Current.MaxRecordingDurationSecs.ShouldBe(90);
        Store.Current.LoggingConfig.LogLevel.ShouldBe("debug");
        Store.Current.StartWithSystem.ShouldBeFalse();
    }

    [Fact]
    public async Task TwoEditsSeparatedByACompletedWindow_ProduceTwoSaves()
    {
        var editor = NewEditor();

        editor.Edit(settings => settings.AudioFormat = AudioFormat.Wav);
        CompleteQuietWindow();
        await WaitForAsync(() => Saves == 1);

        editor.Edit(settings => settings.MaxRecordingDurationSecs = 90);
        CompleteQuietWindow();
        await WaitForAsync(() => Saves == 2);

        Saves.ShouldBe(2);
        Store.Current.AudioFormat.ShouldBe(AudioFormat.Wav);
        Store.Current.MaxRecordingDurationSecs.ShouldBe(90);
    }

    // ---- Task 2.3: the clone is taken per quiet window, not per editor ----

    [Fact]
    public async Task AStaleDraftNeverRevertsAPresetWrittenThroughTheStore()
    {
        // The regression guard for the whole clone decision. An editor that cloned once and kept the
        // draft would save a graph taken before the preset existed, silently deleting it.
        var editor = NewEditor();

        editor.Edit(settings => settings.AudioFormat = AudioFormat.Wav);
        await editor.FlushAsync();

        Store.SavePreset(new Preset {Id = "mine", Name = "Custom", SystemPrompt = "Say it plainly."});

        editor.Edit(settings => settings.MaxRecordingDurationSecs = 90);
        await editor.FlushAsync();

        Store.Current.Presets.ShouldContain(preset => preset.Id == "mine");
        Store.Current.AudioFormat.ShouldBe(AudioFormat.Wav);
        Store.Current.MaxRecordingDurationSecs.ShouldBe(90);
    }

    // ---- Task 2.4: FlushAsync ----

    [Fact]
    public async Task FlushAsync_WritesAPendingDraftBeforeItReturns()
    {
        var editor = NewEditor();
        editor.Edit(settings => settings.AudioFormat = AudioFormat.Wav);

        await editor.FlushAsync();

        Saves.ShouldBe(1);
        Store.Current.AudioFormat.ShouldBe(AudioFormat.Wav);
    }

    [Fact]
    public async Task FlushAsync_WithNothingPending_IsANoOp()
    {
        var editor = NewEditor();

        await editor.FlushAsync();
        await editor.FlushAsync();

        Saves.ShouldBe(0);
    }

    [Fact]
    public async Task FlushAsync_CalledTwice_ProducesOneSave()
    {
        var editor = NewEditor();
        editor.Edit(settings => settings.AudioFormat = AudioFormat.Wav);

        await Task.WhenAll(editor.FlushAsync(), editor.FlushAsync());
        await editor.FlushAsync();

        Saves.ShouldBe(1);
    }

    // ---- Task 2.5: the edit-by-id invariant ----

    [Fact]
    public async Task AnEditMadeAfterACompletedCommit_ReachesTheNextDraft()
    {
        // The test that fails if a delegate captured a reference out of an earlier Current: the
        // second edit would then write into a graph nothing will ever save.
        var editor = NewEditor();
        editor.Edit(settings => settings.Providers.Add(
            new ProviderConfig {Id = "one", ApiKey = "first"}));
        await editor.FlushAsync();

        editor.Edit(settings =>
        {
            var entry = settings.Providers.FirstOrDefault(candidate => candidate.Id == "one");
            if (entry is not null)
            {
                entry.ApiKey = "second";
            }
        });
        await editor.FlushAsync();

        Store.Current.Providers.Single(entry => entry.Id == "one").ApiKey.ShouldBe("second");
    }

    [Fact]
    public async Task AnEditNamingAnIdRemovedEarlierInTheSameWindow_IsANoOpRatherThanAThrow()
    {
        var editor = NewEditor();
        editor.Edit(settings => settings.Providers.Add(
            new ProviderConfig {Id = "one", ApiKey = "first"}));
        await editor.FlushAsync();

        editor.Edit(settings =>
        {
            var entry = settings.Providers.FirstOrDefault(candidate => candidate.Id == "one");
            if (entry is not null)
            {
                entry.ApiKey = "typed";
            }
        });
        editor.Edit(settings => settings.Providers.RemoveAll(candidate => candidate.Id == "one"));
        editor.Edit(settings =>
        {
            var entry = settings.Providers.FirstOrDefault(candidate => candidate.Id == "one");
            if (entry is not null)
            {
                entry.ApiKey = "typed some more";
            }
        });

        await Should.NotThrowAsync(editor.FlushAsync());
        Store.Current.Providers.ShouldBeEmpty();
    }

    // ---- Task 1.1/1.2 (surface-settings-save-failures): a commit that cannot be written ----

    [Fact]
    public async Task ACommitThatCannotBeWritten_NotifiesAndLogsRatherThanThrowingUnobserved()
    {
        var notifications = new RecordingNotificationService();
        var editor = NewEditor(notifications: notifications);

        // Locking the settings file exclusively forces the commit's File.Move to fail with a real
        // IOException, the same shape a network drive going away or another process holding the file
        // would produce — without deleting the test's own temp directory, which Dispose still needs.
        using (File.Open(Store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            editor.Edit(settings => settings.AudioFormat = AudioFormat.Wav);
            CompleteQuietWindow();

            await WaitForAsync(() => notifications.Forced.Count == 1);
        }

        notifications.Forced.ShouldHaveSingleItem();
        notifications.Forced[0].Title.ShouldBe("Settings Not Saved");
        Saves.ShouldBe(0);
        Store.Current.AudioFormat.ShouldBe(AudioFormat.Opus);
    }
}
