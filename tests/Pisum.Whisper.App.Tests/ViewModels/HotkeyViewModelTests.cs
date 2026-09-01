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
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>Tasks 4.8 and 4.9 — the hotkey recorder, and what it does when the hook is not running.</summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class HotkeyViewModelTests : SettingsEditorTestBase
{
    private readonly IGlobalHotkeyService _hotkeys = A.Fake<IGlobalHotkeyService>();

    private readonly Queue<HotkeyCapture> _captures = new();

    private int _captureCalls;

    public HotkeyViewModelTests()
    {
        A.CallTo(() => _hotkeys.Availability).Returns(HotkeyAvailability.Available);
        A.CallTo(() => _hotkeys.CaptureAsync(A<CancellationToken>._))
            .ReturnsLazily(() =>
            {
                _captureCalls++;
                return Task.FromResult(_captures.Count > 0 ? _captures.Dequeue() : HotkeyCapture.Cancelled);
            });
    }

    private HotkeyViewModel NewViewModel(SettingsEditor editor, AppSettings? settings = null)
    {
        return new HotkeyViewModel(
            editor, _hotkeys, NullLogger<HotkeyViewModel>.Instance, settings ?? Store.Current);
    }

    private static HotkeyCapture Captured(string key, params string[] modifiers)
    {
        return new HotkeyCapture(
            HotkeyCaptureOutcome.Captured,
            new HotkeyBinding {Modifiers = [..modifiers], Key = key});
    }

    // ---- Task 4.8: the recorder ----

    [Fact]
    public void TheCurrentBindingIsShown()
    {
        var viewModel = NewViewModel(
            NewEditor(),
            new AppSettings {Hotkey = new HotkeyBinding {Modifiers = ["Ctrl", "Shift"], Key = "Space"}});

        viewModel.Binding.ShouldBe("Ctrl + Shift + Space");
    }

    [Fact]
    public async Task AGoodCapture_ReachesTheDraftAndIsShown()
    {
        _captures.Enqueue(Captured("F9", "Alt", "Shift"));
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        await viewModel.StartRecordingCommand.ExecuteAsync(null);
        await editor.FlushAsync();

        viewModel.Binding.ShouldBe("Alt + Shift + F9");
        viewModel.IsRecording.ShouldBeFalse();
        Store.Current.Hotkey.Modifiers.ShouldBe(["Alt", "Shift"]);
        Store.Current.Hotkey.Key.ShouldBe("F9");
    }

    [Fact]
    public async Task AModifierlessCapture_IsRefusedAndRecordingContinues()
    {
        _captures.Enqueue(Captured("K"));
        _captures.Enqueue(Captured("K", "Ctrl"));
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        await viewModel.StartRecordingCommand.ExecuteAsync(null);
        await editor.FlushAsync();

        // Two captures were consumed, so the bare K did not end the recording.
        _captureCalls.ShouldBe(2);
        viewModel.Binding.ShouldBe("Ctrl + K");
        Store.Current.Hotkey.Key.ShouldBe("K");
    }

    [Fact]
    public async Task BareEscape_CancelsAndLeavesTheBindingUntouched()
    {
        _captures.Enqueue(Captured("Escape"));
        var editor = NewEditor();
        var before = Store.Current.Hotkey.Key;
        var viewModel = NewViewModel(editor);

        await viewModel.StartRecordingCommand.ExecuteAsync(null);
        await editor.FlushAsync();

        _captureCalls.ShouldBe(1);
        viewModel.IsRecording.ShouldBeFalse();
        viewModel.Message.ShouldBeNull();
        Store.Current.Hotkey.Key.ShouldBe(before);
        Saves.ShouldBe(0);
    }

    [Fact]
    public async Task CtrlEscape_IsStillBindable()
    {
        _captures.Enqueue(Captured("Escape", "Ctrl"));
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        await viewModel.StartRecordingCommand.ExecuteAsync(null);
        await editor.FlushAsync();

        viewModel.Binding.ShouldBe("Ctrl + Escape");
        Store.Current.Hotkey.Key.ShouldBe("Escape");
        Store.Current.Hotkey.Modifiers.ShouldBe(["Ctrl"]);
    }

    [Fact]
    public async Task AnUnnameableKey_ProducesAMessageAndRecordingContinues()
    {
        _captures.Enqueue(HotkeyCapture.KeyNotSupported);
        _captures.Enqueue(Captured("F9", "Alt"));
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        var recording = viewModel.StartRecordingCommand.ExecuteAsync(null);
        await recording;

        _captureCalls.ShouldBe(2);
        viewModel.Binding.ShouldBe("Alt + F9");
    }

    [Fact]
    public async Task AnUnnameableKeyAloneLeavesTheMessageOnScreen()
    {
        var release = new TaskCompletionSource<HotkeyCapture>();
        var calls = 0;
        A.CallTo(() => _hotkeys.CaptureAsync(A<CancellationToken>._))
            .ReturnsLazily(() => ++calls == 1
                ? Task.FromResult(HotkeyCapture.KeyNotSupported)
                : release.Task);

        var viewModel = NewViewModel(NewEditor());
        var recording = viewModel.StartRecordingCommand.ExecuteAsync(null);

        viewModel.IsRecording.ShouldBeTrue();
        viewModel.Message.ShouldNotBeNull().ShouldContain("cannot be used as a hotkey");

        release.SetResult(HotkeyCapture.Cancelled);
        await recording;
    }

    [Fact]
    public async Task Cancel_EndsTheCaptureAndTheHideAndDeactivationPathsUseTheSameCall()
    {
        var observed = default(CancellationToken);
        var release = new TaskCompletionSource<HotkeyCapture>();
        A.CallTo(() => _hotkeys.CaptureAsync(A<CancellationToken>._))
            .Invokes((CancellationToken token) => observed = token)
            .Returns(release.Task);

        var viewModel = NewViewModel(NewEditor());
        var recording = viewModel.StartRecordingCommand.ExecuteAsync(null);

        observed.IsCancellationRequested.ShouldBeFalse();

        // The one method Cancel, the window hiding and the window deactivating all call.
        viewModel.Cancel();

        observed.IsCancellationRequested.ShouldBeTrue();

        release.SetResult(HotkeyCapture.Cancelled);
        await recording;
        viewModel.IsRecording.ShouldBeFalse();
    }

    [Fact]
    public async Task ASecondChangeClickWhileRecording_NeitherStartsACaptureNorReadsAsACancel()
    {
        var release = new TaskCompletionSource<HotkeyCapture>();
        A.CallTo(() => _hotkeys.CaptureAsync(A<CancellationToken>._))
            .ReturnsLazily(() =>
            {
                _captureCalls++;
                return release.Task;
            });

        var viewModel = NewViewModel(NewEditor());
        var recording = viewModel.StartRecordingCommand.ExecuteAsync(null);

        viewModel.StartRecordingCommand.CanExecute(null).ShouldBeFalse();
        await viewModel.StartRecordingCommand.ExecuteAsync(null);

        _captureCalls.ShouldBe(1);
        viewModel.IsRecording.ShouldBeTrue();
        viewModel.Message.ShouldBe("Press a key combination...");

        release.SetResult(Captured("F9", "Alt"));
        await recording;
    }

    // ---- Task 4.9: availability and conflicts ----

    [Theory]
    [InlineData(HotkeyAvailability.NotStarted)]
    [InlineData(HotkeyAvailability.PermissionNotGranted)]
    [InlineData(HotkeyAvailability.PermissionRevoked)]
    [InlineData(HotkeyAvailability.Failed)]
    public void ChangeIsDisabledAndABannerNamesTheStateWhenKeysAreNotObserved(HotkeyAvailability availability)
    {
        A.CallTo(() => _hotkeys.Availability).Returns(availability);

        var viewModel = NewViewModel(NewEditor());

        viewModel.IsAvailable.ShouldBeFalse();
        viewModel.StartRecordingCommand.CanExecute(null).ShouldBeFalse();
        viewModel.UnavailableBanner.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NoBannerIsShownWhenKeysAreObserved()
    {
        var viewModel = NewViewModel(NewEditor());

        viewModel.IsAvailable.ShouldBeTrue();
        viewModel.UnavailableBanner.ShouldBeNull();
        viewModel.StartRecordingCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task AConflictingBindingIsStillSavedAndWarnedAbout()
    {
        // Alt+F4 is in ConflictDetector's table.
        _captures.Enqueue(Captured("F4", "Alt"));
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        await viewModel.StartRecordingCommand.ExecuteAsync(null);
        await editor.FlushAsync();

        viewModel.ConflictsWithSystemHotkey.ShouldBeTrue();
        Store.Current.Hotkey.Key.ShouldBe("F4");
        Store.Current.Hotkey.Modifiers.ShouldBe(["Alt"]);
    }

    [Fact]
    public async Task ANonConflictingBindingProducesNoWarning()
    {
        _captures.Enqueue(Captured("F9", "Alt", "Shift"));
        var viewModel = NewViewModel(NewEditor());

        await viewModel.StartRecordingCommand.ExecuteAsync(null);

        viewModel.ConflictsWithSystemHotkey.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void TheViewLoadsAndBinds()
    {
        var viewModel = NewViewModel(NewEditor());
        var window = new Window {Content = new HotkeyView {DataContext = viewModel}};

        window.Show();

        window.GetVisualDescendants().OfType<TextBlock>()
            .Single(block => block.Name == "BindingText")
            .Text.ShouldBe(viewModel.Binding);
        window.GetVisualDescendants().OfType<Button>()
            .ShouldContain(button => Equals(button.Content, "Change"));
    }
}
