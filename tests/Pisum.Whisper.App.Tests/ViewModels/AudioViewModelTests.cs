namespace Pisum.Whisper.App.Tests.ViewModels;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.App.Settings.Views;
using Pisum.Whisper.App.Tests.Settings;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>Task 4.1 — the Audio tab.</summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class AudioViewModelTests : SettingsEditorTestBase
{
    [Fact]
    public void TheCurrentFormatIsPreselected()
    {
        var opus = new AudioViewModel(NewEditor(), new AppSettings {AudioFormat = AudioFormat.Opus});
        var wav = new AudioViewModel(NewEditor(), new AppSettings {AudioFormat = AudioFormat.Wav});

        opus.IsOpus.ShouldBeTrue();
        opus.IsWav.ShouldBeFalse();
        wav.IsWav.ShouldBeTrue();
        wav.IsOpus.ShouldBeFalse();
    }

    [Fact]
    public async Task ChoosingWav_EditsTheDraft()
    {
        var editor = NewEditor();
        var viewModel = new AudioViewModel(editor, Store.Current);

        viewModel.IsWav = true;
        await editor.FlushAsync();

        Store.Current.AudioFormat.ShouldBe(AudioFormat.Wav);
        viewModel.IsOpus.ShouldBeFalse();
    }

    [Fact]
    public async Task ChoosingOpus_EditsTheDraft()
    {
        var editor = NewEditor();
        Store.Save(new AppSettings {AudioFormat = AudioFormat.Wav});
        var viewModel = new AudioViewModel(editor, Store.Current);

        viewModel.IsOpus = true;
        await editor.FlushAsync();

        Store.Current.AudioFormat.ShouldBe(AudioFormat.Opus);
        viewModel.IsWav.ShouldBeFalse();
    }

    [Fact]
    public void UncheckingIsIgnored()
    {
        // A radio group unchecks the outgoing button as well as checking the incoming one. Acting on
        // both would write the same choice twice.
        var editor = NewEditor();
        var viewModel = new AudioViewModel(editor, Store.Current);

        viewModel.IsOpus = false;

        viewModel.IsOpus.ShouldBeTrue();
        Saves.ShouldBe(0);
    }

    [AvaloniaFact]
    public void TheViewLoadsAndBinds()
    {
        var viewModel = new AudioViewModel(NewEditor(), Store.Current);
        var window = new Window {Content = new AudioView {DataContext = viewModel}};

        window.Show();

        var buttons = window.GetVisualDescendants().OfType<RadioButton>().ToList();
        buttons.Count.ShouldBe(2);
        buttons[0].IsChecked.ShouldBe(true);
        buttons[1].IsChecked.ShouldBe(false);
    }
}
