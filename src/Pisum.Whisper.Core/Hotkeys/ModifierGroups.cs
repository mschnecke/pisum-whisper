namespace Pisum.Whisper.Core.Hotkeys;

using SharpHook.Data;

/// <summary>
/// Folds SharpHook's raw event state into the four modifier groups a binding is expressed in.
/// </summary>
/// <remarks>
/// This exists because <see cref="EventMask"/> cannot be compared directly and the two ways of
/// getting that wrong both produce a hotkey that "sometimes does not work":
/// <list type="bullet">
/// <item>
/// Its group values are unions of the two sides — <c>Ctrl</c> is <c>LeftCtrl | RightCtrl</c>, that
/// is <c>0x22</c> — so <c>mask.HasFlag(EventMask.Ctrl)</c> is true only when <b>both</b> Ctrl keys
/// are held. The side-agnostic test is <c>(mask &amp; EventMask.Ctrl) != 0</c>.
/// </item>
/// <item>
/// It also carries Num Lock, Caps Lock, Scroll Lock and the five mouse buttons, so any equality
/// test against the raw mask fails whenever Caps Lock happens to be on or a mouse button happens to
/// be held.
/// </item>
/// </list>
/// Folding to groups first makes an equality comparison correct, which is what the binding needs:
/// exactly these modifiers, no more and no fewer.
/// </remarks>
public static class ModifierGroups
{
    /// <summary>Reduces an event's modifier state to the groups a binding can require.</summary>
    public static HotkeyModifiers FromEventMask(EventMask mask)
    {
        var groups = HotkeyModifiers.None;

        if ((mask & EventMask.Shift) != 0)
        {
            groups |= HotkeyModifiers.Shift;
        }

        if ((mask & EventMask.Ctrl) != 0)
        {
            groups |= HotkeyModifiers.Ctrl;
        }

        if ((mask & EventMask.Alt) != 0)
        {
            groups |= HotkeyModifiers.Alt;
        }

        if ((mask & EventMask.Meta) != 0)
        {
            groups |= HotkeyModifiers.Meta;
        }

        return groups;
    }

    /// <summary>
    /// Reports which group a key belongs to, or <see cref="HotkeyModifiers.None"/> if it is not a
    /// modifier key. This is how a release is attributed to the chord: the mask on a release event
    /// cannot be relied on to have shed the key that is being released.
    /// </summary>
    public static HotkeyModifiers FromKeyCode(KeyCode keyCode) => keyCode switch
    {
        KeyCode.VcLeftShift or KeyCode.VcRightShift => HotkeyModifiers.Shift,
        KeyCode.VcLeftControl or KeyCode.VcRightControl => HotkeyModifiers.Ctrl,
        KeyCode.VcLeftAlt or KeyCode.VcRightAlt => HotkeyModifiers.Alt,
        KeyCode.VcLeftMeta or KeyCode.VcRightMeta => HotkeyModifiers.Meta,
        _ => HotkeyModifiers.None,
    };
}
