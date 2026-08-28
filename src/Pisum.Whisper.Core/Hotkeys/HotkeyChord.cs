namespace Pisum.Whisper.Core.Hotkeys;

using Pisum.Whisper.Core.Settings;
using SharpHook.Data;

/// <summary>
/// A binding compiled into the form the matcher compares against: a set of modifier groups and one
/// key code. Immutable, so the running hook can be handed a new one with a single volatile write
/// rather than being stopped and restarted.
/// </summary>
public sealed record HotkeyChord(HotkeyModifiers Modifiers, KeyCode Key)
{
    /// <summary>
    /// The binding used when the configured one cannot be compiled — Ctrl+Shift+Space, or
    /// Cmd+Shift+Space on macOS. Taken from <see cref="HotkeyBinding"/>'s own defaults rather than
    /// restated, so there is one definition of "the default binding" in the application.
    /// </summary>
    public static HotkeyChord Default { get; } = CompileDefault();

    /// <summary>
    /// Compiles a binding, reporting the first token it could not resolve. It never throws: an
    /// unresolvable binding is a hand-edited settings file, and the caller falls back rather than
    /// failing.
    /// </summary>
    public static bool TryCompile(HotkeyBinding binding, out HotkeyChord chord, out string invalidToken)
    {
        // Not Default: this method is what builds Default, so referring to it here would read a
        // static that is still being initialised. On failure the caller substitutes Default itself.
        chord = new HotkeyChord(HotkeyModifiers.None, KeyCode.VcUndefined);
        invalidToken = string.Empty;

        var modifiers = HotkeyModifiers.None;

        foreach (var name in binding.Modifiers)
        {
            // Blank entries are skipped rather than rejected, following the reference, so a stray
            // comma in a hand-edited file does not cost the user their hotkey.
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!KeyCodeMap.TryParseModifier(name, out var modifier))
            {
                invalidToken = name;
                return false;
            }

            modifiers |= modifier;
        }

        if (!KeyCodeMap.TryParseKey(binding.Key, out var key))
        {
            invalidToken = binding.Key;
            return false;
        }

        chord = new HotkeyChord(modifiers, key);
        return true;
    }

    /// <summary>
    /// Renders the chord for a log line. The binding itself is safe to log; other keys are not.
    /// The order matches <see cref="KeyCodeMap.GetModifierNames"/>, so a chord reads the same in a
    /// log line as it does in the settings file.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>(5);

        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Meta))
        {
            parts.Add("Meta");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(KeyCodeMap.TryGetKeyName(Key, out var name) ? name : Key.ToString());

        return string.Join("+", parts);
    }

    private static HotkeyChord CompileDefault()
    {
        // HotkeyBinding's own default is per-OS. If this ever fails to compile, the two vocabularies
        // have diverged and every binding would silently fall back to something that does not exist,
        // so it throws rather than papering over it.
        if (!TryCompile(new HotkeyBinding(), out var chord, out var invalidToken))
        {
            throw new InvalidOperationException(
                $"The default hotkey binding does not compile: '{invalidToken}' is not a known token.");
        }

        return chord;
    }
}
