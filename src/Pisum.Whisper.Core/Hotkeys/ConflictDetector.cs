namespace Pisum.Whisper.Core.Hotkeys;

using Pisum.Whisper.Core.Settings;
using SharpHook.Data;

/// <summary>
/// Reports whether a binding collides with a shortcut the operating system is likely to own, ported
/// from the reference's <c>hotkey/conflict.rs</c>.
/// </summary>
/// <remarks>
/// <para>
/// It warns and never blocks. The table is a heuristic — it mixes Windows and macOS shortcuts, and
/// users have legitimate reasons to override any of them — so nothing in the runtime path consults
/// this. The settings window calls it to show a warning next to a binding the user has chosen.
/// </para>
/// <para>
/// Two entries describe combinations that never reach a low-level hook at all: Ctrl+Alt+Delete and
/// Win+L are handled by the Windows kernel as secure attention sequences. Binding them fails
/// silently rather than conflicting, which is a better reason to warn about them, not a worse one.
/// </para>
/// </remarks>
public static class ConflictDetector
{
    // The reference's table, with its modifiers folded into groups. Folding makes the comparison
    // order-insensitive by construction, which is what the reference achieves by sorting both sides.
    private static readonly (HotkeyModifiers Modifiers, KeyCode Key)[] SystemHotkeys =
    [
        // Windows
        (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, KeyCode.VcDelete),
        (HotkeyModifiers.Alt, KeyCode.VcTab),
        (HotkeyModifiers.Alt, KeyCode.VcF4),
        (HotkeyModifiers.Meta, KeyCode.VcL),
        (HotkeyModifiers.Meta, KeyCode.VcD),
        (HotkeyModifiers.Meta, KeyCode.VcE),
        (HotkeyModifiers.Meta, KeyCode.VcR),
        (HotkeyModifiers.Meta, KeyCode.VcTab),
        (HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, KeyCode.VcEscape),

        // macOS. Cmd+Tab folds onto the same entry as Win+Tab above; the reference lists it twice
        // and that duplication is kept rather than tidied, so the table still reads as two lists.
        (HotkeyModifiers.Meta, KeyCode.VcQ),
        (HotkeyModifiers.Meta, KeyCode.VcW),
        (HotkeyModifiers.Meta, KeyCode.VcTab),
        (HotkeyModifiers.Meta | HotkeyModifiers.Shift, KeyCode.Vc3),
        (HotkeyModifiers.Meta | HotkeyModifiers.Shift, KeyCode.Vc4),
        (HotkeyModifiers.Meta | HotkeyModifiers.Shift, KeyCode.Vc5),
        (HotkeyModifiers.Meta, KeyCode.VcSpace),
        (HotkeyModifiers.Ctrl, KeyCode.VcSpace),
    ];

    /// <summary>
    /// Reports whether <paramref name="binding"/> matches a known system shortcut. A binding this
    /// vocabulary cannot resolve never conflicts: it cannot equal a shortcut whose every part is
    /// known.
    /// </summary>
    public static bool ConflictsWithSystemHotkey(HotkeyBinding binding)
    {
        if (!TryNormalise(binding, out var modifiers, out var key))
        {
            return false;
        }

        foreach (var (systemModifiers, systemKey) in SystemHotkeys)
        {
            if (systemModifiers == modifiers && systemKey == key)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Folds a binding to its group set and key code. Empty modifier entries are skipped rather than
    /// rejected, following the reference, so a file with a stray blank still compares sensibly.
    /// </summary>
    private static bool TryNormalise(HotkeyBinding binding, out HotkeyModifiers modifiers, out KeyCode key)
    {
        modifiers = HotkeyModifiers.None;
        key = KeyCode.VcUndefined;

        foreach (var name in binding.Modifiers)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!KeyCodeMap.TryParseModifier(name, out var modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        return KeyCodeMap.TryParseKey(binding.Key, out key);
    }
}
