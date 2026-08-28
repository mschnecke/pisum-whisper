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
[TestClass]
public sealed class HookProviderProbeTests
{
    [TestMethod]
    public async Task PostedEvent_KeepsItsMaskAndIsNotFlaggedAsSimulated()
    {
        var provider = new TestProvider(TestThreadingMode.Simple);
        using var hook = new SimpleGlobalHook(provider);

        KeyboardHookEventArgs? observed = null;
        hook.KeyPressed += (_, e) => observed = e;

        var running = hook.RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true);
        while (!hook.IsRunning)
        {
            await Task.Delay(5);
        }

        var posted = new UioHookEvent
        {
            Type = EventType.KeyPressed,
            Mask = EventMask.LeftCtrl | EventMask.LeftShift,
            Keyboard = new KeyboardEventData { KeyCode = KeyCode.VcSpace },
        };

        provider.PostEvent(ref posted).ShouldBe(UioHookResult.Success);

        hook.Stop();
        await running;

        observed.ShouldNotBeNull();
        observed.Data.KeyCode.ShouldBe(KeyCode.VcSpace);
        observed.RawEvent.Mask.ShouldBe(EventMask.LeftCtrl | EventMask.LeftShift);
        observed.IsEventSimulated.ShouldBeFalse();
    }

    [TestMethod]
    public async Task SuppressedEvent_IsRecordedByTheProvider()
    {
        var provider = new TestProvider(TestThreadingMode.Simple);
        using var hook = new SimpleGlobalHook(provider);

        hook.KeyPressed += (_, e) => e.SuppressEvent = true;

        var running = hook.RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true);
        while (!hook.IsRunning)
        {
            await Task.Delay(5);
        }

        var posted = new UioHookEvent
        {
            Type = EventType.KeyPressed,
            Keyboard = new KeyboardEventData { KeyCode = KeyCode.VcSpace },
        };

        provider.PostEvent(ref posted);

        hook.Stop();
        await running;

        provider.SuppressedEvents.Count.ShouldBe(1);
    }
}
