namespace Pisum.Whisper.App.Tests.Views;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.App.Tests;
using Pisum.Whisper.App.Tests.Settings;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Shell;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Tasks 3.1, 3.3 and 3.6, and the deactivation half of task 4.8 — the window shell itself.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class SettingsWindowTests : SettingsEditorTestBase
{
    private readonly IGlobalHotkeyService _hotkeys = A.Fake<IGlobalHotkeyService>();

    private SettingsWindowViewModel NewViewModel(SettingsEditor editor)
    {
        A.CallTo(() => _hotkeys.Availability).Returns(HotkeyAvailability.Available);

        return new SettingsWindowViewModel(
            Store,
            editor,
            A.Fake<IGeminiKeyProbe>(),
            _hotkeys,
            new LogDirectory(Path.Combine(Path.GetTempPath(), "pisum-whisper-tests-logs")),
            A.Fake<ISystemShell>(),
            new RecordingNotificationService(),
            NullLoggerFactory.Instance);
    }

    [AvaloniaFact]
    public void TheWindowHasItsSixTabsAndItsSize()
    {
        var window = new SettingsWindow(NewViewModel(NewEditor()));

        window.Show();

        window.Width.ShouldBe(700);
        window.Height.ShouldBe(540);
        window.MinWidth.ShouldBe(540);
        window.MinHeight.ShouldBe(400);
        window.CanResize.ShouldBeTrue();
        window.CanMaximize.ShouldBeFalse();
        window.WindowStartupLocation.ShouldBe(WindowStartupLocation.CenterScreen);
        window.Title.ShouldBe("Pisum Whisper Settings");
        window.RequestedThemeVariant.ShouldBe(ThemeVariant.Light);

        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.Items.OfType<TabItem>().Select(tab => tab.Header)
            .ShouldBe(["Providers", "Presets", "Hotkey", "Audio", "Logging", "General"]);
    }

    [AvaloniaFact]
    public void ClosingTheWindow_HidesItAndLeavesItAlive()
    {
        var window = new SettingsWindow(NewViewModel(NewEditor()));
        window.Show();

        window.Close();

        window.IsVisible.ShouldBeFalse();

        // Still alive: showing it again is the same instance, so a partly typed entry survives.
        window.Show();
        window.IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void ClosingForApplicationShutdown_IsNotCancelled()
    {
        // The one that would otherwise be found only by a user unable to quit: a window that refuses
        // every close hangs the process on Quit.
        var window = new SettingsWindow(NewViewModel(NewEditor()));
        window.Show();

        var cancelled = WindowInternals.Close(window, WindowCloseReason.ApplicationShutdown);

        cancelled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void ClosingByTheCloseButton_IsCancelledInFavourOfHiding()
    {
        var window = new SettingsWindow(NewViewModel(NewEditor()));
        window.Show();

        var cancelled = WindowInternals.Close(window, WindowCloseReason.WindowClosing);

        cancelled.ShouldBeTrue();
        window.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task ClosingTheWindow_FlushesAPendingEdit()
    {
        var editor = NewEditor();
        var window = new SettingsWindow(NewViewModel(editor));
        window.Show();

        editor.Edit(settings => settings.AudioFormat = AudioFormat.Wav);
        Saves.ShouldBe(0);

        window.Close();

        await WaitForAsync(() => Saves == 1);
        Store.Current.AudioFormat.ShouldBe(AudioFormat.Wav);
    }

    [AvaloniaFact]
    public async Task DeactivatingTheWindow_CancelsAnOpenCapture()
    {
        // An open capture suspends hotkey matching process-wide, so a user who clicks Change and then
        // switches to another application would otherwise be left with a dead hotkey and nothing
        // saying why.
        var release = new TaskCompletionSource<HotkeyCapture>();
        var observed = default(CancellationToken);
        A.CallTo(() => _hotkeys.CaptureAsync(A<CancellationToken>._))
            .Invokes((CancellationToken token) => observed = token)
            .Returns(release.Task);

        var viewModel = NewViewModel(NewEditor());
        var window = new SettingsWindow(viewModel);
        window.Show();

        var recording = viewModel.Hotkey.StartRecordingCommand.ExecuteAsync(null);
        observed.IsCancellationRequested.ShouldBeFalse();

        // The same callback the platform makes when the user clicks away to another application.
        WindowInternals.Deactivate(window);

        observed.IsCancellationRequested.ShouldBeTrue();

        release.SetResult(HotkeyCapture.Cancelled);
        await recording;
    }

    [AvaloniaFact]
    public async Task HidingTheWindow_CancelsAnOpenCapture()
    {
        var release = new TaskCompletionSource<HotkeyCapture>();
        var observed = default(CancellationToken);
        A.CallTo(() => _hotkeys.CaptureAsync(A<CancellationToken>._))
            .Invokes((CancellationToken token) => observed = token)
            .Returns(release.Task);

        var viewModel = NewViewModel(NewEditor());
        var window = new SettingsWindow(viewModel);
        window.Show();

        var recording = viewModel.Hotkey.StartRecordingCommand.ExecuteAsync(null);
        window.Close();

        observed.IsCancellationRequested.ShouldBeTrue();

        release.SetResult(HotkeyCapture.Cancelled);
        await recording;
    }
}
