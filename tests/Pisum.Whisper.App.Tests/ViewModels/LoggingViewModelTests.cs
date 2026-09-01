namespace Pisum.Whisper.App.Tests.ViewModels;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.App.Settings.Views;
using Pisum.Whisper.App.Tests.Settings;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Shell;
using Shouldly;

/// <summary>Task 4.3 — the Logging tab.</summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class LoggingViewModelTests : SettingsEditorTestBase
{
    private const string LogPath = @"C:\logs\pisum-whisper";

    private readonly ISystemShell _shell = A.Fake<ISystemShell>();

    private LoggingViewModel NewViewModel(SettingsEditor editor, AppSettings? settings = null)
    {
        return new LoggingViewModel(
            editor,
            new LogDirectory(LogPath),
            _shell,
            NullLogger<LoggingViewModel>.Instance,
            settings ?? Store.Current);
    }

    [Fact]
    public void TheCurrentLevelAndPathAreShown()
    {
        var viewModel = NewViewModel(
            NewEditor(),
            new AppSettings {LoggingConfig = new LoggingConfig {LogLevel = "debug"}});

        viewModel.LogLevel.ShouldBe("debug");
        viewModel.LogDirectoryPath.ShouldBe(LogPath);
        viewModel.LogLevels.ShouldBe(["trace", "debug", "info", "warn", "error"]);
    }

    [Fact]
    public void AnUnrecognisedLevelInTheFile_FallsBackToTheOneTheLoggerWillUse()
    {
        var viewModel = NewViewModel(
            NewEditor(),
            new AppSettings {LoggingConfig = new LoggingConfig {LogLevel = "shouty"}});

        viewModel.LogLevel.ShouldBe("info");
    }

    [Fact]
    public async Task ChoosingALevel_ReachesTheDraft()
    {
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        viewModel.LogLevel = "trace";
        await editor.FlushAsync();

        Store.Current.LoggingConfig.LogLevel.ShouldBe("trace");
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    [InlineData("0", 1)]
    [InlineData("-2", 1)]
    [InlineData("101", 100)]
    [InlineData("", 1)]
    [InlineData("big", 1)]
    public async Task TheFileSizeIsConfinedToItsBounds(string typed, int expected)
    {
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        viewModel.MaxFileSizeMb = typed;
        await editor.FlushAsync();

        Store.Current.LoggingConfig.LogMaxFileSizeMb.ShouldBe(expected);
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData("1", 1)]
    [InlineData("365", 365)]
    [InlineData("0", 1)]
    [InlineData("366", 365)]
    [InlineData("", 1)]
    [InlineData("forever", 1)]
    public async Task TheRetentionIsConfinedToItsBounds(string typed, int expected)
    {
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        viewModel.RetentionDays = typed;
        await editor.FlushAsync();

        Store.Current.LoggingConfig.LogRetentionDays.ShouldBe(expected);
    }

    [Fact]
    public void OpenLogFolder_AsksTheShellForTheLogDirectory()
    {
        var viewModel = NewViewModel(NewEditor());

        viewModel.OpenLogFolderCommand.Execute(null);

        A.CallTo(() => _shell.OpenFolder(LogPath)).MustHaveHappenedOnceExactly();
        viewModel.OpenFailure.ShouldBeNull();
    }

    [Fact]
    public void OpenLogFolder_ReportsAFailureAndLeavesTheWindowUsable()
    {
        A.CallTo(() => _shell.OpenFolder(A<string>._))
            .Throws(new SystemShellException("No file browser is registered."));

        var viewModel = NewViewModel(NewEditor());

        Should.NotThrow(() => viewModel.OpenLogFolderCommand.Execute(null));

        viewModel.OpenFailure.ShouldNotBeNull().ShouldContain("No file browser is registered.");
    }

    [Fact]
    public void ConstructingTheViewModel_WritesNothing()
    {
        _ = NewViewModel(NewEditor());

        Saves.ShouldBe(0);
    }

    [AvaloniaFact]
    public void TheViewLoadsAndBinds()
    {
        var viewModel = NewViewModel(NewEditor());
        var window = new Window {Content = new LoggingView {DataContext = viewModel}};

        window.Show();

        window.GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem.ShouldBe("info");
        var boxes = window.GetVisualDescendants().OfType<TextBox>().ToList();
        boxes.Single(box => box.Name == "MaxFileSizeBox").Text.ShouldBe("1");
        boxes.Single(box => box.Name == "RetentionDaysBox").Text.ShouldBe("7");
        window.GetVisualDescendants().OfType<Button>()
            .ShouldContain(button => Equals(button.Content, "Open Log Folder"));
    }
}
