namespace Pisum.Whisper.Core.Tests.Hotkeys;

using SharpHook.Data;
using Shouldly;

/// <summary>
/// Tasks 3.9 and 3.10 — what this component is allowed to write down.
/// </summary>
/// <remarks>
/// It observes every keystroke on the machine in order to match one combination, and change 10 puts
/// an "Open Log Folder" button a click away. The privacy assertions below are therefore load-bearing
/// rather than decorative: the obvious debugging statement in this component is a keylog.
/// </remarks>
[IntegrationTest]
public sealed class GlobalHotkeyLoggingTests : GlobalHotkeyServiceTestBase
{
    // ---- Task 3.9: libuiohook's own diagnostics reach the log, at warning and above ----

    [Fact]
    public async Task LibUioHookWarnings_ReachTheLog()
    {
        await StartAsync();

        LogSource.Raise(LogLevel.Warn, "hook thread is falling behind");

        WaitForLogMessageContaining("hook thread is falling behind").ShouldBeTrue();
    }

    [Fact]
    public async Task LibUioHookErrors_ReachTheLog()
    {
        await StartAsync();

        LogSource.Raise(LogLevel.Error, "failed to create event port");

        WaitForLogMessageContaining("failed to create event port").ShouldBeTrue();
    }

    [Fact]
    public async Task LibUioHookDebugAndInfo_DoNotReachTheLog()
    {
        await StartAsync();
        var before = LogMessages.Count;

        // libuiohook logs per event at these levels. Forwarding them would defeat the whole of the
        // section below.
        LogSource.Raise(LogLevel.Debug, "key 0x20 pressed");
        LogSource.Raise(LogLevel.Info, "key 0x20 released");

        await Task.Delay(100, TestContext.Current.CancellationToken);
        LogMessages.Count.ShouldBe(before);
    }

    [Fact]
    public async Task DisposingTheService_DisposesTheLogSource()
    {
        await StartAsync();

        Service.Dispose();

        LogSource.IsDisposed.ShouldBeTrue();
    }

    // ---- Task 3.10: no keystroke is ever written down ----

    [Fact]
    public async Task TypingIsNeverLogged_EvenAtTheMostVerboseLevel()
    {
        await StartAsync();
        var before = LogMessages.Count;

        // Fifty keystrokes of "typing" in another application, with modifiers in play.
        KeyCode[] typed =
        [
            KeyCode.VcH, KeyCode.VcE, KeyCode.VcL, KeyCode.VcL, KeyCode.VcO,
            KeyCode.VcComma, KeyCode.VcSpace, KeyCode.VcW, KeyCode.VcO, KeyCode.VcR,
            KeyCode.VcL, KeyCode.VcD, KeyCode.VcNumPad7, KeyCode.VcF4, KeyCode.VcQ,
            KeyCode.VcSemicolon, KeyCode.VcSlash, KeyCode.VcBackQuote, KeyCode.VcJ, KeyCode.VcZ,
            KeyCode.Vc1, KeyCode.Vc2, KeyCode.Vc3, KeyCode.VcTab, KeyCode.VcEnter,
        ];

        foreach (var key in typed)
        {
            Press(key, EventMask.None);
            Release(key, EventMask.None);
        }

        await Task.Delay(150, TestContext.Current.CancellationToken);

        LogMessages.Count.ShouldBe(before, "not one of those keystrokes belongs in a log file");

        // Nothing anywhere in the log may name a key code, whatever else may have been written.
        foreach (var message in LogMessages)
        {
            foreach (var name in Enum.GetNames<KeyCode>())
            {
                message.ShouldNotContain(name);
            }
        }
    }

    [Fact]
    public async Task TheBindingAndItsEdgesAreLogged_AndNothingElseIs()
    {
        await StartAsync();

        Press(KeyCode.VcSpace);
        Release(KeyCode.VcSpace);
        WaitForEdges(2).ShouldBeTrue();

        // RecordingSink calls RenderMessage(), which quotes scalar values; the application's own
        // sinks use {Message:lj}, which does not. Asserting on the parts keeps this test about what
        // is logged rather than about how one sink renders it.
        WaitForLogMessageContaining("Ctrl+Shift+Space").ShouldBeTrue();
        LogMessages.ShouldContain(message => message.Contains("Pressed", StringComparison.Ordinal));
        LogMessages.ShouldContain(message => message.Contains("Released", StringComparison.Ordinal));

        // The binding is named as a binding, never as the key codes it was matched from.
        foreach (var message in LogMessages)
        {
            message.ShouldNotContain("VcSpace");
            message.ShouldNotContain("KeyCode");
        }
    }

    [Fact]
    public async Task UnmatchedModifiersAreNotLogged()
    {
        await StartAsync();
        var before = LogMessages.Count;

        Press(KeyCode.VcLeftControl, EventMask.LeftCtrl);
        Press(KeyCode.VcLeftShift);
        Release(KeyCode.VcLeftShift, EventMask.LeftCtrl);
        Release(KeyCode.VcLeftControl, EventMask.None);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        LogMessages.Count.ShouldBe(before);
    }
}
