namespace Pisum.Whisper.Core.Hotkeys;

/// <summary>
/// The four modifier groups a binding can require, each covering both the left and the right
/// instance of its key.
/// </summary>
/// <remarks>
/// This is deliberately not <c>SharpHook.Data.EventMask</c>. That enum distinguishes left from
/// right — <c>Ctrl</c> is <c>LeftCtrl | RightCtrl</c>, so <c>HasFlag(EventMask.Ctrl)</c> is true only
/// when both are held — and it also carries the lock keys and the mouse buttons, which no binding
/// should be sensitive to. Folding a mask into these four groups is what makes an equality
/// comparison correct.
/// </remarks>
[Flags]
public enum HotkeyModifiers
{
    None = 0,

    Shift = 1,

    Ctrl = 2,

    Alt = 4,

    Meta = 8,
}
