namespace Pisum.Whisper.Core.Tests.Hotkeys;

using Pisum.Whisper.Core.Hotkeys;
using SharpHook.Data;
using Shouldly;

/// <summary>
/// These are the two mistakes that produce a hotkey which works on one machine and not on another:
/// treating <c>EventMask</c>'s group values as single bits, and comparing a raw mask that also
/// carries the lock keys and the mouse buttons.
/// </summary>
public sealed class ModifierGroupsTests
{
    [Fact]
    public void LeftAndRightModifiers_FoldToTheSameGroup()
    {
        ModifierGroups.FromEventMask(EventMask.LeftCtrl).ShouldBe(HotkeyModifiers.Ctrl);
        ModifierGroups.FromEventMask(EventMask.RightCtrl).ShouldBe(HotkeyModifiers.Ctrl);
        ModifierGroups.FromEventMask(EventMask.LeftShift).ShouldBe(HotkeyModifiers.Shift);
        ModifierGroups.FromEventMask(EventMask.RightShift).ShouldBe(HotkeyModifiers.Shift);
        ModifierGroups.FromEventMask(EventMask.LeftAlt).ShouldBe(HotkeyModifiers.Alt);
        ModifierGroups.FromEventMask(EventMask.RightAlt).ShouldBe(HotkeyModifiers.Alt);
        ModifierGroups.FromEventMask(EventMask.LeftMeta).ShouldBe(HotkeyModifiers.Meta);
        ModifierGroups.FromEventMask(EventMask.RightMeta).ShouldBe(HotkeyModifiers.Meta);
    }

    [Fact]
    public void HasFlag_WouldHaveFailedTheRightHandCase()
    {
        // The trap this fold exists to avoid, asserted directly so it cannot creep back in:
        // EventMask.Ctrl is LeftCtrl | RightCtrl, so HasFlag demands both keys at once.
        EventMask.Ctrl.ShouldBe(EventMask.LeftCtrl | EventMask.RightCtrl);

        EventMask.RightCtrl.HasFlag(EventMask.Ctrl).ShouldBeFalse();
        ModifierGroups.FromEventMask(EventMask.RightCtrl).ShouldBe(HotkeyModifiers.Ctrl);
    }

    [Fact]
    public void LockKeys_AreDiscarded()
    {
        var mask = EventMask.LeftCtrl | EventMask.LeftShift
                   | EventMask.CapsLock | EventMask.NumLock | EventMask.ScrollLock;

        ModifierGroups.FromEventMask(mask).ShouldBe(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
    }

    [Fact]
    public void MouseButtons_AreDiscarded()
    {
        var mask = EventMask.LeftCtrl | EventMask.LeftShift
                   | EventMask.Button1 | EventMask.Button3 | EventMask.Button5;

        ModifierGroups.FromEventMask(mask).ShouldBe(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
    }

    [Fact]
    public void SimulatedFlag_IsDiscarded()
    {
        var mask = EventMask.LeftCtrl | EventMask.SimulatedEvent;

        ModifierGroups.FromEventMask(mask).ShouldBe(HotkeyModifiers.Ctrl);
    }

    [Fact]
    public void EmptyMask_FoldsToNone()
    {
        ModifierGroups.FromEventMask(EventMask.None).ShouldBe(HotkeyModifiers.None);
    }

    [Fact]
    public void MixedSides_FoldToOneGroupEach()
    {
        var mask = EventMask.LeftCtrl | EventMask.RightShift;

        ModifierGroups.FromEventMask(mask).ShouldBe(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
    }
}
