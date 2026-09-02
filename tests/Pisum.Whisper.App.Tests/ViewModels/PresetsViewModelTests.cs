namespace Pisum.Whisper.App.Tests.ViewModels;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.App.Settings.Views;
using Pisum.Whisper.App.Tests;
using Pisum.Whisper.App.Tests.Settings;
using Pisum.Whisper.Core.Notifications;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>Task 4.7 — the Presets tab.</summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class PresetsViewModelTests : SettingsEditorTestBase
{
    private PresetsViewModel NewViewModel(SettingsEditor editor, INotificationService? notifications = null)
    {
        return new PresetsViewModel(
            Store, editor, notifications ?? new RecordingNotificationService(), NullLogger<PresetsViewModel>.Instance);
    }

    private static void Fill(PresetsViewModel viewModel, string name, string prompt)
    {
        viewModel.NewName = name;
        viewModel.NewSystemPrompt = prompt;
    }

    [Fact]
    public void ThePresetsAreListedWithTheirBadges()
    {
        Store.SavePreset(new Preset {Id = "mine", Name = "Custom", SystemPrompt = "Say it plainly."});
        Store.SetActivePreset("mine");

        var viewModel = NewViewModel(NewEditor());

        viewModel.Presets.Select(preset => preset.Id).ShouldBe(["de-transcribe", "en-transcribe", "mine"]);
        viewModel.Presets.Where(preset => preset.IsBuiltin).Select(preset => preset.Id)
            .ShouldBe(["de-transcribe", "en-transcribe"]);
        viewModel.Presets.Single(preset => preset.IsActive).Id.ShouldBe("mine");
    }

    [Fact]
    public async Task Add_StoresThePresetAndFlushesFirst()
    {
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        // A pending draft cloned before the add. Without the flush the store's save would be
        // overwritten by this draft, silently deleting the preset that was just added.
        editor.Edit(settings => settings.MaxRecordingDurationSecs = 90);

        Fill(viewModel, "Notes", "Turn my dictation into bullet points.");
        await viewModel.AddCommand.ExecuteAsync(null);

        Store.Current.Presets.ShouldContain(preset => preset.Name == "Notes");
        Store.Current.MaxRecordingDurationSecs.ShouldBe(90);

        await editor.FlushAsync();
        Store.Current.Presets.ShouldContain(preset => preset.Name == "Notes");

        viewModel.NewName.ShouldBeEmpty();
        viewModel.NewSystemPrompt.ShouldBeEmpty();
        viewModel.Presets.ShouldContain(preset => preset.Name == "Notes");
    }

    [Fact]
    public async Task Add_WhenTheWriteFails_KeepsTheTypedFieldsAndNotifies()
    {
        var notifications = new RecordingNotificationService();
        var viewModel = NewViewModel(NewEditor(), notifications);
        Fill(viewModel, "Notes", "Turn my dictation into bullet points.");

        // Locking the settings file exclusively forces the store's write to fail, the same shape a
        // disk-full or permission-denied failure would take.
        using (File.Open(Store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await viewModel.AddCommand.ExecuteAsync(null);
        }

        Store.Current.Presets.ShouldNotContain(preset => preset.Name == "Notes");
        viewModel.NewName.ShouldBe("Notes");
        viewModel.NewSystemPrompt.ShouldBe("Turn my dictation into bullet points.");
        notifications.Forced.ShouldHaveSingleItem();
        notifications.Forced[0].Title.ShouldBe("Settings Not Saved");
    }

    [Fact]
    public async Task Save_StoresTheEditedNameAndPromptAndFlushesFirst()
    {
        Store.SavePreset(new Preset {Id = "mine", Name = "Custom", SystemPrompt = "Say it plainly."});
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);
        editor.Edit(settings => settings.MaxRecordingDurationSecs = 90);

        viewModel.Selected = viewModel.Presets.Single(preset => preset.Id == "mine");
        viewModel.Selected.Name = "Renamed";
        viewModel.Selected.SystemPrompt = "Be terse.";
        await viewModel.SaveCommand.ExecuteAsync(null);

        var stored = Store.Current.Presets.Single(preset => preset.Id == "mine");
        stored.Name.ShouldBe("Renamed");
        stored.SystemPrompt.ShouldBe("Be terse.");
        Store.Current.MaxRecordingDurationSecs.ShouldBe(90);
    }

    [Fact]
    public async Task Save_WhenTheWriteFails_RevertsTheDisplayedTextAndNotifies()
    {
        Store.SavePreset(new Preset {Id = "mine", Name = "Custom", SystemPrompt = "Say it plainly."});
        var notifications = new RecordingNotificationService();
        var viewModel = NewViewModel(NewEditor(), notifications);

        viewModel.Selected = viewModel.Presets.Single(preset => preset.Id == "mine");
        viewModel.Selected.Name = "Renamed";
        viewModel.Selected.SystemPrompt = "Be terse.";

        using (File.Open(Store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await viewModel.SaveCommand.ExecuteAsync(null);
        }

        var stored = Store.Current.Presets.Single(preset => preset.Id == "mine");
        stored.Name.ShouldBe("Custom");
        stored.SystemPrompt.ShouldBe("Say it plainly.");

        // The regression guard for the issue: the bound text reverts to what is actually persisted
        // rather than continuing to show the edit that failed to save.
        viewModel.Selected.ShouldNotBeNull();
        viewModel.Selected.Name.ShouldBe("Custom");
        viewModel.Selected.SystemPrompt.ShouldBe("Say it plainly.");

        notifications.Forced.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Save_KeepsABuiltinBuiltIn()
    {
        var viewModel = NewViewModel(NewEditor());

        viewModel.Selected = viewModel.Presets.Single(preset => preset.Id == "de-transcribe");
        viewModel.Selected.Name = "Diktat";
        await viewModel.SaveCommand.ExecuteAsync(null);

        var stored = Store.Current.Presets.Single(preset => preset.Id == "de-transcribe");
        stored.Name.ShouldBe("Diktat");
        stored.IsBuiltin.ShouldBeTrue();
    }

    [Fact]
    public async Task Activate_SwitchesTheActivePresetAndFlushesFirst()
    {
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);
        editor.Edit(settings => settings.MaxRecordingDurationSecs = 90);

        viewModel.Selected = viewModel.Presets.Single(preset => preset.Id == "de-transcribe");
        await viewModel.ActivateCommand.ExecuteAsync(null);

        Store.Current.ActivePresetId.ShouldBe("de-transcribe");
        Store.Current.MaxRecordingDurationSecs.ShouldBe(90);
        viewModel.Presets.Single(preset => preset.IsActive).Id.ShouldBe("de-transcribe");
    }

    [Fact]
    public async Task Activate_WhenTheWriteFails_LeavesTheActivePresetAndNotifies()
    {
        var notifications = new RecordingNotificationService();
        var viewModel = NewViewModel(NewEditor(), notifications);
        var before = Store.Current.ActivePresetId;

        viewModel.Selected = viewModel.Presets.Single(preset => preset.Id == "de-transcribe");

        using (File.Open(Store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await viewModel.ActivateCommand.ExecuteAsync(null);
        }

        Store.Current.ActivePresetId.ShouldBe(before);
        notifications.Forced.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Delete_RemovesAUserPresetAndFlushesFirst()
    {
        Store.SavePreset(new Preset {Id = "mine", Name = "Custom", SystemPrompt = "Say it plainly."});
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);
        editor.Edit(settings => settings.MaxRecordingDurationSecs = 90);

        viewModel.Selected = viewModel.Presets.Single(preset => preset.Id == "mine");
        await viewModel.DeleteCommand.ExecuteAsync(null);

        Store.Current.Presets.ShouldNotContain(preset => preset.Id == "mine");
        Store.Current.MaxRecordingDurationSecs.ShouldBe(90);
        viewModel.Presets.ShouldNotContain(preset => preset.Id == "mine");
    }

    [Fact]
    public async Task Delete_WhenTheWriteFails_LeavesThePresetAndNotifies()
    {
        Store.SavePreset(new Preset {Id = "mine", Name = "Custom", SystemPrompt = "Say it plainly."});
        var notifications = new RecordingNotificationService();
        var viewModel = NewViewModel(NewEditor(), notifications);

        viewModel.Selected = viewModel.Presets.Single(preset => preset.Id == "mine");

        using (File.Open(Store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await viewModel.DeleteCommand.ExecuteAsync(null);
        }

        Store.Current.Presets.ShouldContain(preset => preset.Id == "mine");
        viewModel.Presets.ShouldContain(preset => preset.Id == "mine");
        notifications.Forced.ShouldHaveSingleItem();
    }

    [Fact]
    public void ABuiltinOffersNoDelete()
    {
        var viewModel = NewViewModel(NewEditor());

        viewModel.Selected = viewModel.Presets.Single(preset => preset.Id == "de-transcribe");

        viewModel.Selected.CanDelete.ShouldBeFalse();
        viewModel.DeleteCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task TheStoresRefusalToDeleteABuiltin_IsNotReachableFromTheUi()
    {
        var viewModel = NewViewModel(NewEditor());
        viewModel.Selected = viewModel.Presets.Single(preset => preset.Id == "en-transcribe");

        // Invoked directly, which is the path a disabled button does not cover. The command's own
        // guard is what makes the store's *validation* SettingsException unreachable; a try still
        // exists in DeleteAsync for a write that fails to reach disk, which no guard can rule out.
        await Should.NotThrowAsync(viewModel.DeleteCommand.ExecuteAsync(null));

        Store.Current.Presets.ShouldContain(preset => preset.Id == "en-transcribe");
    }

    [Theory]
    [InlineData("", "A prompt.")]
    [InlineData("   ", "A prompt.")]
    [InlineData("A name", "")]
    [InlineData("A name", "   ")]
    [InlineData("", "")]
    public void Add_IsDisabledForABlankNameOrPrompt(string name, string prompt)
    {
        var viewModel = NewViewModel(NewEditor());

        Fill(viewModel, name, prompt);

        viewModel.AddCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Add_IsEnabledOnceBothFieldsCarryText()
    {
        var viewModel = NewViewModel(NewEditor());

        Fill(viewModel, "Notes", "Turn my dictation into bullet points.");

        viewModel.AddCommand.CanExecute(null).ShouldBeTrue();
    }

    [Theory]
    [InlineData("", "A prompt.")]
    [InlineData("   ", "A prompt.")]
    [InlineData("A name", "")]
    [InlineData("A name", "   ")]
    public void Save_IsDisabledForABlankNameOrPrompt(string name, string prompt)
    {
        var viewModel = NewViewModel(NewEditor());
        viewModel.Selected = viewModel.Presets[0];

        viewModel.Selected.Name = name;
        viewModel.Selected.SystemPrompt = prompt;

        viewModel.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("", "A prompt.")]
    [InlineData("   ", "A prompt.")]
    [InlineData("A name", "")]
    [InlineData("A name", "   ")]
    public async Task ABlankPresetIsNotSavedEvenWhenTheCommandIsInvokedDirectly(string name, string prompt)
    {
        // A preset with an empty prompt would become selectable and be sent to Gemini as the
        // instruction for the user's speech, which the store does nothing to prevent.
        var viewModel = NewViewModel(NewEditor());
        Fill(viewModel, name, prompt);

        await viewModel.AddCommand.ExecuteAsync(null);

        Store.Current.Presets.Count.ShouldBe(2);
        Store.Current.Presets.ShouldAllBe(preset => preset.SystemPrompt.Length > 0);
    }

    [Fact]
    public async Task DeletingTheActivePreset_LeavesAnActiveOne()
    {
        Store.SavePreset(new Preset {Id = "mine", Name = "Custom", SystemPrompt = "Say it plainly."});
        Store.SetActivePreset("mine");
        var viewModel = NewViewModel(NewEditor());

        viewModel.Selected = viewModel.Presets.Single(preset => preset.Id == "mine");
        await viewModel.DeleteCommand.ExecuteAsync(null);

        Store.Current.Presets.ShouldContain(preset => preset.Id == Store.Current.ActivePresetId);
        viewModel.Presets.Count(preset => preset.IsActive).ShouldBe(1);
    }

    [AvaloniaFact]
    public void TheViewLoadsAndBinds()
    {
        var viewModel = NewViewModel(NewEditor());
        var window = new Window {Content = new PresetsView {DataContext = viewModel}};

        window.Show();

        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        list.ItemCount.ShouldBe(2);

        var boxes = window.GetVisualDescendants().OfType<TextBox>().ToList();
        boxes.Single(box => box.Name == "SelectedNameBox").Text.ShouldBe(viewModel.Presets[0].Name);
        boxes.Single(box => box.Name == "NewNameBox").Text.ShouldBeNullOrEmpty();
    }
}
