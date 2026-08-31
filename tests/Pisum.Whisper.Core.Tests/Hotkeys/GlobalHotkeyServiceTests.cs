namespace Pisum.Whisper.Core.Tests.Hotkeys;

using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Settings;
using SharpHook.Data;
using Shouldly;

public sealed class GlobalHotkeyServiceTests : GlobalHotkeyServiceTestBase
{
    // ---- Task 3.1: the binding is observed through a real hook over a fake provider ----

    [Fact]
    public async Task MatchingCombination_RaisesOnePressAndOneRelease()
    {
        await StartAsync();

        Press(KeyCode.VcSpace);
        Release(KeyCode.VcSpace);

        WaitForEdges(2).ShouldBeTrue();
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);
    }

    [Fact]
    public async Task NonMatchingKeys_RaiseNothing()
    {
        await StartAsync();

        Press(KeyCode.VcA);
        Release(KeyCode.VcA);
        Press(KeyCode.VcSpace, EventMask.None);
        Release(KeyCode.VcSpace, EventMask.None);

        await Task.Delay(150, TestContext.Current.CancellationToken);
        Observed().ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchedKey_IsWithheldFromTheFocusedApplication()
    {
        await StartAsync();

        Press(KeyCode.VcSpace);
        Release(KeyCode.VcSpace);
        WaitForEdges(2).ShouldBeTrue();

        // Both edges of the main key, and nothing else.
        Provider.SuppressedEvents.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ModifierKeys_AreNotWithheld()
    {
        await StartAsync();

        Press(KeyCode.VcLeftControl, EventMask.LeftCtrl);
        Press(KeyCode.VcLeftShift, CtrlShift);
        Press(KeyCode.VcSpace);
        Release(KeyCode.VcLeftShift, EventMask.LeftCtrl);
        Release(KeyCode.VcLeftControl, EventMask.None);

        WaitForEdges(2).ShouldBeTrue();

        // Only the main key's press. The modifiers all reached the focused application.
        Provider.SuppressedEvents.Count.ShouldBe(1);
    }

    // ---- Task 3.2: the hook thread is never held by a consumer ----

    [Fact]
    public async Task SlowConsumer_DoesNotHoldTheHookThread()
    {
        var handlerEntered = new ManualResetEventSlim();
        Service.Pressed += (_, _) =>
        {
            handlerEntered.Set();
            Thread.Sleep(TimeSpan.FromSeconds(2));
        };

        await StartAsync();

        var pressCost = Post(EventType.KeyPressed, KeyCode.VcSpace, CtrlShift);
        handlerEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue("the press should have reached the consumer");

        var releaseCost = Post(EventType.KeyReleased, KeyCode.VcSpace, CtrlShift);

        // If the events were raised on the hook thread, posting would have cost the two seconds the
        // handler sleeps. Windows removes a low-level hook that takes that long.
        pressCost.ShouldBeLessThan(TimeSpan.FromMilliseconds(500));
        releaseCost.ShouldBeLessThan(TimeSpan.FromMilliseconds(500));

        WaitForEdges(2).ShouldBeTrue();
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released], "edges must stay in order");
    }

    [Fact]
    public async Task ThrowingConsumer_DoesNotStopTheDispatchLoop()
    {
        Service.Pressed += (_, _) => throw new InvalidOperationException("consumer defect");

        await StartAsync();

        Press(KeyCode.VcSpace);
        Release(KeyCode.VcSpace);

        // The Released is very likely the edge that ends a recording, so it must survive a handler
        // that throws on the Pressed before it.
        WaitForEdges(2).ShouldBeTrue();
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);
    }

    // ---- Task 3.3: lifecycle ----

    [Fact]
    public async Task Start_RunsAKeyboardOnlyHook()
    {
        await StartAsync();

        Provider.IsRunning.ShouldBeTrue();
        Provider.GlobalHookType.ShouldBe(GlobalHookType.Keyboard);
        Service.Availability.ShouldBe(HotkeyAvailability.Available);
    }

    [Fact]
    public async Task Stop_StopsTheHookAndEndsTheDispatchLoop()
    {
        await StartAsync();

        await Service.StopAsync(CancellationToken.None);

        Provider.IsRunning.ShouldBeFalse();

        // StopAsync awaits the dispatch loop, so its completion is the assertion: a leaked loop
        // would have left StopAsync hanging until the test timed out.
        Press(KeyCode.VcSpace);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Observed().ShouldBeEmpty();
    }

    // ---- Task 3.4: a release is always paid ----

    [Fact]
    public async Task HookDisabledWhileHeld_SynthesisesARelease()
    {
        await StartAsync();
        Press(KeyCode.VcSpace);
        WaitForEdges(1).ShouldBeTrue();

        // Stopping the hook is what raises HookDisabled; the physical key-up is never seen.
        Service.Dispose();

        WaitForEdges(2).ShouldBeTrue();
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);
    }

    [Fact]
    public async Task StopWhileHeld_SynthesisesARelease()
    {
        await StartAsync();
        Press(KeyCode.VcSpace);
        WaitForEdges(1).ShouldBeTrue();

        await Service.StopAsync(CancellationToken.None);

        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);
    }

    [Fact]
    public async Task DisposeWhileHeld_SynthesisesExactlyOneRelease()
    {
        await StartAsync();
        Press(KeyCode.VcSpace);
        WaitForEdges(1).ShouldBeTrue();

        Service.Dispose();
        Service.Dispose();

        WaitForEdges(2).ShouldBeTrue();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);
    }

    [Fact]
    public async Task StopWhileIdle_OwesNothing()
    {
        await StartAsync();

        await Service.StopAsync(CancellationToken.None);

        Observed().ShouldBeEmpty();
    }

    // ---- Task 3.5: a denied permission is reported, not fatal ----

    [Fact]
    public async Task AccessibilityNeverGranted_StartsAnywayAndReportsWhy()
    {
        Provider.RunResult = UioHookResult.ErrorAxApiDisabled;

        await Should.NotThrowAsync(StartAsync);

        Service.Availability.ShouldBe(HotkeyAvailability.PermissionNotGranted);
        WaitForLogMessageContaining("has not been granted").ShouldBeTrue();
    }

    [Fact]
    public async Task AccessibilityWithdrawn_IsReportedDistinctlyFromNeverGranted()
    {
        Provider.RunResult = UioHookResult.ErrorAxApiRevoked;

        await Should.NotThrowAsync(StartAsync);

        Service.Availability.ShouldBe(HotkeyAvailability.PermissionRevoked);

        // The remedies differ, so the two must not share a message.
        WaitForLogMessageContaining("was withdrawn").ShouldBeTrue();
        LogMessages.ShouldNotContain(message => message.Contains("has not been granted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OtherStartupFailure_IsReportedAsAFailureRatherThanAPermission()
    {
        Provider.RunResult = UioHookResult.ErrorSetWindowsHookEx;

        await Should.NotThrowAsync(StartAsync);

        Service.Availability.ShouldBe(HotkeyAvailability.Failed);
        WaitForLogMessageContaining("ErrorSetWindowsHookEx").ShouldBeTrue();
    }

    [Fact]
    public async Task FailedStart_LeavesTheApplicationUsable()
    {
        Provider.RunResult = UioHookResult.ErrorAxApiDisabled;
        await StartAsync();

        // Nothing is observed, but stopping and disposing must still be well-behaved: the host will
        // do both on the way out.
        await Should.NotThrowAsync(() => Service.StopAsync(CancellationToken.None));
        Observed().ShouldBeEmpty();
    }

    [Fact]
    public async Task HookThatNeverStarts_DoesNotHangStartup()
    {
        // Change 1's macOS spike, on an Apple M4 with no Accessibility grant: libuiohook neither
        // failed nor prompted, it blocked at zero CPU with the tap never installed. Waiting only on
        // HookEnabled would hang host startup for good, and this process has no window to say so.
        using var service = new GlobalHotkeyService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalHotkeyService>.Instance,
            Settings,
            new RecordingLogSource(),
            new BlockingHookProvider(),
            TimeSpan.FromMilliseconds(300));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StartAsync(CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "startup must not wait on the hook");
        service.Availability.ShouldBe(HotkeyAvailability.Failed);
    }

    // ---- Task 3.6: rebinding without restarting the hook ----

    [Fact]
    public async Task SavingANewBinding_SwitchesWhatIsObserved()
    {
        await StartAsync();

        var settings = Settings.Current;
        settings.Hotkey = Binding("F9", "Alt");
        Settings.Save(settings);

        Service.Chord.ShouldBe(new HotkeyChord(HotkeyModifiers.Alt, KeyCode.VcF9));

        Press(KeyCode.VcSpace);
        Release(KeyCode.VcSpace);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Observed().ShouldBeEmpty("the old binding must stop being observed");

        Press(KeyCode.VcF9, EventMask.LeftAlt);
        Release(KeyCode.VcF9, EventMask.LeftAlt);
        WaitForEdges(2).ShouldBeTrue();
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);
    }

    [Fact]
    public async Task Rebinding_DoesNotRestartTheHook()
    {
        await StartAsync();

        var settings = Settings.Current;
        settings.Hotkey = Binding("F9", "Alt");
        Settings.Save(settings);

        Provider.IsRunning.ShouldBeTrue("the hook must keep running across a rebind");
    }

    [Fact]
    public async Task RebindingWhileHeld_ReleasesTheOldBindingFirst()
    {
        await StartAsync();
        Press(KeyCode.VcSpace);
        WaitForEdges(1).ShouldBeTrue();

        var settings = Settings.Current;
        settings.Hotkey = Binding("F9", "Alt");
        Settings.Save(settings);

        WaitForEdges(2).ShouldBeTrue();
        Observed().ShouldBe([HotkeyEdge.Pressed, HotkeyEdge.Released]);
    }

    // ---- Task 3.7: an unusable binding falls back rather than disabling the hotkey ----

    [Fact]
    public async Task UnparseableBinding_FallsBackToTheDefault()
    {
        WriteSettings(new AppSettings { Hotkey = Binding("Nonsense", "Ctrl") });

        var settings = new SettingsStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsStore>.Instance, SettingsPath);
        settings.Load();
        settings.Current.Hotkey = Binding("Nonsense", "Ctrl");
        Settings.Save(settings.Current);

        Service.Chord.ShouldBe(HotkeyChord.Default);

        WaitForLogMessageContaining("Nonsense").ShouldBeTrue("the warning must name the offending token");
        await Task.CompletedTask;
    }

    [Fact]
    public void UnparseableBinding_LeavesTheSettingsFileAlone()
    {
        WriteSettings(new AppSettings { Hotkey = Binding("Hyper", "Ctrl") });
        var before = File.ReadAllBytes(SettingsPath);

        var settings = new SettingsStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsStore>.Instance, SettingsPath);
        settings.Load();

        using var service = new GlobalHotkeyService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalHotkeyService>.Instance,
            settings,
            new SharpHook.Logging.EmptyLogSource(),
            new SharpHook.Testing.TestProvider(SharpHook.Testing.TestThreadingMode.Simple));

        service.Chord.ShouldBe(HotkeyChord.Default);
        File.ReadAllBytes(SettingsPath).ShouldBe(before, "SettingsStore owns every write to the file");
    }
}
