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

        using var dispatched = new ManualResetEventSlim();
        KeyboardHookEventArgs? observed = null;
        hook.KeyPressed += (_, e) =>
        {
            observed = e;
            dispatched.Set();
        };

        var running = hook.RunAsync(GlobalHookType.Keyboard, true);
        while (!hook.IsRunning)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        var posted = new UioHookEvent
        {
            Type = EventType.KeyPressed,
            Mask = EventMask.LeftCtrl | EventMask.LeftShift,
            Keyboard = new KeyboardEventData {KeyCode = KeyCode.VcSpace},
        };

        provider.PostEvent(ref posted).ShouldBe(UioHookResult.Success);

        // Waiting on the handler, not on IsRunning. A started hook has not necessarily dispatched a
        // posted event yet, and stopping before it does drops the event silently — PostEvent still
        // answers Success, so the failure surfaces four lines down as a null. Sequentially that
        // race was never lost; under xUnit's parallel classes it was, about once in 25 runs.
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

        using var dispatched = new ManualResetEventSlim();
        hook.KeyPressed += (_, e) =>
        {
            e.SuppressEvent = true;
            dispatched.Set();
        };

        var running = hook.RunAsync(GlobalHookType.Keyboard, true);
        while (!hook.IsRunning)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        var posted = new UioHookEvent
        {
            Type = EventType.KeyPressed,
            Keyboard = new KeyboardEventData {KeyCode = KeyCode.VcSpace},
        };

        provider.PostEvent(ref posted);

        // Same race as above: without this the handler that sets SuppressEvent may never run.
        dispatched.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ShouldBeTrue("the posted event should have reached the hook");

        hook.Stop();
        await running;

        provider.SuppressedEvents.Count.ShouldBe(1);
    }
}
