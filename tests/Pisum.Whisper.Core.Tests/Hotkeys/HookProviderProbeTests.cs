namespace Pisum.Whisper.Core.Tests.Hotkeys;

using SharpHook;
using SharpHook.Data;
using SharpHook.Testing;
using Shouldly;

/// <summary>
/// Pins how <see cref="TestProvider"/> presents an event to a hook, because the service's behaviour
/// depends on two things the package does not document: whether a posted event keeps the mask it was
/// given, and whether it arrives flagged as simulated. If either changes, the service tests would
/// start passing for the wrong reason.
/// </summary>
[UnitTest]
public sealed class HookProviderProbeTests
{
    [Fact]
    public async Task PostedEvent_KeepsItsMaskAndIsNotFlaggedAsSimulated()
    {
        var provider = new TestProvider();
        using var hook = new SimpleGlobalHook(provider);

        using var enabled = new ManualResetEventSlim();
        using var dispatched = new ManualResetEventSlim();
        hook.HookEnabled += (_, _) => enabled.Set();

        KeyboardHookEventArgs? observed = null;
        hook.KeyPressed += (_, e) =>
        {
            observed = e;
            dispatched.Set();
        };

        var running = hook.RunAsync(GlobalHookType.Keyboard, true);

        // HookEnabled, not IsRunning. IsRunning turns true before the provider's dispatch proc is
        // installed, and an event posted in that window is answered with Success and then dropped
        // for good — no later wait recovers it. Sequentially the window was never hit; with the
        // thread pool contended by parallel test classes it is, measured at 2 in 3000.
        enabled.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ShouldBeTrue("the hook should have reported itself enabled");

        var posted = new UioHookEvent
        {
            Type = EventType.KeyPressed,
            Mask = EventMask.LeftCtrl | EventMask.LeftShift,
            Keyboard = new KeyboardEventData {KeyCode = KeyCode.VcSpace},
        };

        provider.PostEvent(ref posted).ShouldBe(UioHookResult.Success);

        // And the handler has to have run before Stop, which does not wait for what is in flight.
        dispatched.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ShouldBeTrue("the posted event should have reached the hook");

        hook.Stop();
        await running;

        observed.ShouldNotBeNull();
        observed.Data.KeyCode.ShouldBe(KeyCode.VcSpace);
        observed.RawEvent.Mask.ShouldBe(EventMask.LeftCtrl | EventMask.LeftShift);
        observed.IsEventSimulated.ShouldBeFalse();
    }

    [Fact]
    public async Task SuppressedEvent_IsRecordedByTheProvider()
    {
        var provider = new TestProvider();
        using var hook = new SimpleGlobalHook(provider);

        using var enabled = new ManualResetEventSlim();
        using var dispatched = new ManualResetEventSlim();
        hook.HookEnabled += (_, _) => enabled.Set();

        hook.KeyPressed += (_, e) =>
        {
            e.SuppressEvent = true;
            dispatched.Set();
        };

        var running = hook.RunAsync(GlobalHookType.Keyboard, true);

        // HookEnabled, not IsRunning. IsRunning turns true before the provider's dispatch proc is
        // installed, and an event posted in that window is answered with Success and then dropped
        // for good — no later wait recovers it. Sequentially the window was never hit; with the
        // thread pool contended by parallel test classes it is, measured at 2 in 3000.
        enabled.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ShouldBeTrue("the hook should have reported itself enabled");

        var posted = new UioHookEvent
        {
            Type = EventType.KeyPressed,
            Keyboard = new KeyboardEventData {KeyCode = KeyCode.VcSpace},
        };

        provider.PostEvent(ref posted);

        // As above: Stop does not wait for an in-flight dispatch.
        dispatched.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ShouldBeTrue("the posted event should have reached the hook");

        hook.Stop();
        await running;

        provider.SuppressedEvents.Count.ShouldBe(1);
    }
}
