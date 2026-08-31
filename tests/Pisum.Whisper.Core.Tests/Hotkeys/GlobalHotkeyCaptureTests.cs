namespace Pisum.Whisper.Core.Tests.Hotkeys;

using Pisum.Whisper.Core.Hotkeys;
using SharpHook.Data;
using Shouldly;

/// <summary>
/// Task 3.8 — the entry point change 10's hotkey recorder uses. It reuses this observation rather
/// than starting a second one, because libuiohook keeps one static callback per process.
/// </summary>
public sealed class GlobalHotkeyCaptureTests : GlobalHotkeyServiceTestBase
{
    [Fact]
    public async Task Capture_ReportsTheCombinationInSettingsSpelling()
    {
        await StartAsync();

        var capture = Service.CaptureAsync(CancellationToken.None);

        Press(KeyCode.VcLeftControl, EventMask.LeftCtrl);
        Press(KeyCode.VcLeftShift, CtrlShift);
        Press(KeyCode.VcF9, CtrlShift);

        var result = await capture;

        result.Outcome.ShouldBe(HotkeyCaptureOutcome.Captured);
        result.Binding.ShouldNotBeNull();
        result.Binding.Modifiers.ShouldBe(["Ctrl", "Shift"]);
        result.Binding.Key.ShouldBe("F9");
    }

    [Fact]
    public async Task CapturedBinding_CompilesBackToWhatWasPressed()
    {
        await StartAsync();

        var capture = Service.CaptureAsync(CancellationToken.None);
        Press(KeyCode.VcF9, EventMask.LeftAlt);

        var result = await capture;

        HotkeyChord.TryCompile(result.Binding!, out var chord, out _).ShouldBeTrue();
        chord.ShouldBe(new HotkeyChord(HotkeyModifiers.Alt, KeyCode.VcF9));
    }

    [Fact]
    public async Task ModifiersAlone_DoNotEndACapture()
    {
        await StartAsync();

        var capture = Service.CaptureAsync(CancellationToken.None);

        Press(KeyCode.VcLeftControl, EventMask.LeftCtrl);
        Press(KeyCode.VcLeftAlt, EventMask.LeftCtrl | EventMask.LeftAlt);

        capture.IsCompleted.ShouldBeFalse("a modifier on its own is not a combination");

        Press(KeyCode.VcJ, EventMask.LeftCtrl | EventMask.LeftAlt);
        (await capture).Binding!.Key.ShouldBe("J");
    }

    [Fact]
    public async Task ConfiguredBinding_IsCapturedRatherThanReportedAsAnEdge()
    {
        await StartAsync();

        var capture = Service.CaptureAsync(CancellationToken.None);
        Press(KeyCode.VcSpace);
        Release(KeyCode.VcSpace);

        var result = await capture;

        result.Outcome.ShouldBe(HotkeyCaptureOutcome.Captured);
        result.Binding!.Key.ShouldBe("Space");

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Observed().ShouldBeEmpty("the binding must not fire while it is being re-recorded");
    }

    [Fact]
    public async Task KeyOutsideTheVocabulary_IsReportedAsUnsupported()
    {
        await StartAsync();

        var capture = Service.CaptureAsync(CancellationToken.None);
        Press(KeyCode.VcF13, EventMask.None);

        var result = await capture;

        result.Outcome.ShouldBe(HotkeyCaptureOutcome.KeyNotSupported);
        result.Binding.ShouldBeNull("a key that cannot be named cannot be persisted");
    }

    [Fact]
    public async Task Cancellation_EndsTheCaptureAndResumesMatching()
    {
        await StartAsync();

        using var cancellation = new CancellationTokenSource();
        var capture = Service.CaptureAsync(cancellation.Token);
        await cancellation.CancelAsync();

        (await capture).Outcome.ShouldBe(HotkeyCaptureOutcome.Cancelled);

        Press(KeyCode.VcSpace);
        Release(KeyCode.VcSpace);

        WaitForEdges(2).ShouldBeTrue();
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);
    }

    [Fact]
    public async Task CaptureCompleting_ResumesMatching()
    {
        await StartAsync();

        var capture = Service.CaptureAsync(CancellationToken.None);
        Press(KeyCode.VcF9, EventMask.None);
        await capture;

        // The binding has not been changed — capture only reports it — so the old one is still what
        // is observed once capture ends.
        Press(KeyCode.VcSpace);
        Release(KeyCode.VcSpace);

        WaitForEdges(2).ShouldBeTrue();
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);
    }

    [Fact]
    public async Task StartingACaptureWhileHeld_PaysTheOwedRelease()
    {
        await StartAsync();
        Press(KeyCode.VcSpace);
        WaitForEdges(1).ShouldBeTrue();

        var capture = Service.CaptureAsync(CancellationToken.None);

        WaitForEdges(2).ShouldBeTrue();
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);

        Press(KeyCode.VcF9, EventMask.None);
        await capture;
    }
}
