namespace Pisum.Whisper.App.Tests.ViewModels;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.App.Settings.Views;
using Pisum.Whisper.App.Tests.Settings;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>Task 4.2 — the General tab.</summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class GeneralViewModelTests : SettingsEditorTestBase
{
    [Fact]
    public void TheCurrentModeIsPreselected()
    {
        var hold = new GeneralViewModel(NewEditor(), new AppSettings {RecordingMode = RecordingMode.HoldToRecord});
        var toggle = new GeneralViewModel(NewEditor(), new AppSettings {RecordingMode = RecordingMode.Toggle});

        hold.IsHoldToRecord.ShouldBeTrue();
        toggle.IsToggle.ShouldBeTrue();
        toggle.IsHoldToRecord.ShouldBeFalse();
    }

    [Fact]
    public async Task ChoosingToggle_EditsTheDraft()
    {
        var editor = NewEditor();
        var viewModel = new GeneralViewModel(editor, Store.Current);

        viewModel.IsToggle = true;
        await editor.FlushAsync();

        Store.Current.RecordingMode.ShouldBe(RecordingMode.Toggle);
    }

    [Fact]
    public async Task ChoosingHoldToRecord_EditsTheDraft()
    {
        var editor = NewEditor();
        Store.Save(new AppSettings {RecordingMode = RecordingMode.Toggle});
        var viewModel = new GeneralViewModel(editor, Store.Current);

        viewModel.IsHoldToRecord = true;
        await editor.FlushAsync();

        Store.Current.RecordingMode.ShouldBe(RecordingMode.HoldToRecord);
    }

    [Theory]
    [InlineData("120", 120)]
    [InlineData("10", 10)]
    [InlineData("3600", 3600)]
    [InlineData("9", 10)]
    [InlineData("0", 10)]
    [InlineData("-5", 10)]
    [InlineData("3601", 3600)]
    [InlineData("99999", 3600)]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData("soon", 10)]
    public async Task TheDurationIsConfinedToItsBounds(string typed, int expected)
    {
        var editor = NewEditor();
        var viewModel = new GeneralViewModel(editor, Store.Current);

        viewModel.MaxRecordingDurationSecs = typed;
        await editor.FlushAsync();

        Store.Current.MaxRecordingDurationSecs.ShouldBe(expected);
    }

    [Fact]
    public async Task BothToggles_ReachTheDraft()
    {
        var editor = NewEditor();
        var viewModel = new GeneralViewModel(editor, Store.Current);

        viewModel.StartWithSystem = false;
        viewModel.ShowTrayNotifications = false;
        await editor.FlushAsync();

        Store.Current.StartWithSystem.ShouldBeFalse();
        Store.Current.ShowTrayNotifications.ShouldBeFalse();
    }

    [Fact]
    public void ConstructingTheViewModel_WritesNothing()
    {
        // The fields are seeded directly rather than through the generated setters, or opening the
        // window would save every value it displays.
        _ = new GeneralViewModel(NewEditor(), Store.Current);

        Saves.ShouldBe(0);
    }

    [AvaloniaFact]
    public void TheViewLoadsAndBinds()
    {
        var viewModel = new GeneralViewModel(NewEditor(), Store.Current);
        var window = new Window {Content = new GeneralView {DataContext = viewModel}};

        window.Show();

        window.GetVisualDescendants().OfType<RadioButton>().Count().ShouldBe(2);
        window.GetVisualDescendants().OfType<CheckBox>().Count().ShouldBe(2);
        window.GetVisualDescendants().OfType<TextBox>().Single().Text.ShouldBe("600");
    }
}
