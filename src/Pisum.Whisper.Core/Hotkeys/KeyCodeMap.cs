namespace Pisum.Whisper.Core.Hotkeys;

using System.Collections.Frozen;
using SharpHook.Data;

/// <summary>
/// The vocabulary of key names the <c>hotkey.key</c> setting accepts, ported from the reference's
/// <c>hotkey/parse.rs</c>. Matching is case-insensitive because the settings file is hand-editable,
/// and the reference's short aliases are carried across so a file written for it still loads here.
/// </summary>
/// <remarks>
/// <para>
/// SharpHook's names differ from the reference's throughout — <c>Vc1</c> rather than <c>Digit1</c>,
/// <c>VcOpenBracket</c> rather than <c>BracketLeft</c>, <c>VcEquals</c> rather than <c>Equal</c> —
/// so this is a re-expression against a different enum, not a transcription of one.
/// </para>
/// <para>
/// The map runs in both directions. The reverse direction is what lets a combination captured from
/// the keyboard be written back into settings, and it is deliberately partial: SharpHook reports
/// keys this vocabulary has no name for, and a key that cannot be named is a key that cannot be
/// persisted. Reporting that is the point — inventing a name would put a spelling into the settings
/// file that nothing else understands.
/// </para>
/// </remarks>
public static class KeyCodeMap
{
    private static readonly FrozenDictionary<string, KeyCode> KeysByName;

    private static readonly FrozenDictionary<KeyCode, string> NamesByKeyCode;

    private static readonly FrozenDictionary<string, HotkeyModifiers> ModifiersByName =
        new Dictionary<string, HotkeyModifiers>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = HotkeyModifiers.Ctrl,
            ["Control"] = HotkeyModifiers.Ctrl,
            ["Alt"] = HotkeyModifiers.Alt,
            ["Shift"] = HotkeyModifiers.Shift,
            ["Meta"] = HotkeyModifiers.Meta,
            ["Super"] = HotkeyModifiers.Meta,
            ["Win"] = HotkeyModifiers.Meta,
            ["Cmd"] = HotkeyModifiers.Meta,
            ["Command"] = HotkeyModifiers.Meta,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    static KeyCodeMap()
    {
        var byName = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase);
        var byKeyCode = new Dictionary<KeyCode, string>();

        // Canonical names are declared once and feed both directions; aliases are parse-only. Two
        // separate tables would drift, and the drift would be silent in one direction.
        void Canonical(string name, KeyCode code)
        {
            byName[name] = code;
            byKeyCode[code] = name;
        }

        void Alias(string name, KeyCode code)
        {
            byName[name] = code;
        }

        // The letter, digit, function and numpad ranges are generated rather than written out.
        // Enum.Parse on a constructed name fails loudly at type initialisation if SharpHook ever
        // renames a member, and the tests pin the boundaries of each range.
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            Canonical(letter.ToString(), Enum.Parse<KeyCode>($"Vc{letter}"));
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            var code = Enum.Parse<KeyCode>($"Vc{digit}");
            Canonical(digit.ToString(), code);
            Alias($"Digit{digit}", code);
            Canonical($"Numpad{digit}", Enum.Parse<KeyCode>($"VcNumPad{digit}"));
        }

        for (var number = 1; number <= 12; number++)
        {
            Canonical($"F{number}", Enum.Parse<KeyCode>($"VcF{number}"));
        }

        Canonical("Space", KeyCode.VcSpace);
        Alias(" ", KeyCode.VcSpace);
        Canonical("Enter", KeyCode.VcEnter);
        Alias("Return", KeyCode.VcEnter);
        Canonical("Tab", KeyCode.VcTab);
        Canonical("Escape", KeyCode.VcEscape);
        Alias("Esc", KeyCode.VcEscape);
        Canonical("Backspace", KeyCode.VcBackspace);
        Canonical("Delete", KeyCode.VcDelete);
        Alias("Del", KeyCode.VcDelete);
        Canonical("Insert", KeyCode.VcInsert);
        Alias("Ins", KeyCode.VcInsert);
        Canonical("Home", KeyCode.VcHome);
        Canonical("End", KeyCode.VcEnd);
        Canonical("PageUp", KeyCode.VcPageUp);
        Alias("PgUp", KeyCode.VcPageUp);
        Canonical("PageDown", KeyCode.VcPageDown);
        Alias("PgDn", KeyCode.VcPageDown);

        Canonical("Up", KeyCode.VcUp);
        Alias("ArrowUp", KeyCode.VcUp);
        Canonical("Down", KeyCode.VcDown);
        Alias("ArrowDown", KeyCode.VcDown);
        Canonical("Left", KeyCode.VcLeft);
        Alias("ArrowLeft", KeyCode.VcLeft);
        Canonical("Right", KeyCode.VcRight);
        Alias("ArrowRight", KeyCode.VcRight);

        Canonical("Minus", KeyCode.VcMinus);
        Alias("-", KeyCode.VcMinus);
        Canonical("Equal", KeyCode.VcEquals);
        Alias("=", KeyCode.VcEquals);
        Canonical("BracketLeft", KeyCode.VcOpenBracket);
        Alias("[", KeyCode.VcOpenBracket);
        Canonical("BracketRight", KeyCode.VcCloseBracket);
        Alias("]", KeyCode.VcCloseBracket);
        Canonical("Backslash", KeyCode.VcBackslash);
        Alias("\\", KeyCode.VcBackslash);
        Canonical("Semicolon", KeyCode.VcSemicolon);
        Alias(";", KeyCode.VcSemicolon);
        Canonical("Quote", KeyCode.VcQuote);
        Alias("'", KeyCode.VcQuote);
        Canonical("BackQuote", KeyCode.VcBackQuote);
        Alias("`", KeyCode.VcBackQuote);
        Canonical("Comma", KeyCode.VcComma);
        Alias(",", KeyCode.VcComma);
        Canonical("Period", KeyCode.VcPeriod);
        Alias(".", KeyCode.VcPeriod);
        Canonical("Slash", KeyCode.VcSlash);
        Alias("/", KeyCode.VcSlash);

        Canonical("NumpadAdd", KeyCode.VcNumPadAdd);
        Alias("Numpad+", KeyCode.VcNumPadAdd);
        Canonical("NumpadSubtract", KeyCode.VcNumPadSubtract);
        Alias("Numpad-", KeyCode.VcNumPadSubtract);
        Canonical("NumpadMultiply", KeyCode.VcNumPadMultiply);
        Alias("Numpad*", KeyCode.VcNumPadMultiply);
        Canonical("NumpadDivide", KeyCode.VcNumPadDivide);
        Alias("Numpad/", KeyCode.VcNumPadDivide);
        Canonical("NumpadDecimal", KeyCode.VcNumPadDecimal);
        Alias("Numpad.", KeyCode.VcNumPadDecimal);
        Canonical("NumpadEnter", KeyCode.VcNumPadEnter);

        KeysByName = byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        NamesByKeyCode = byKeyCode.ToFrozenDictionary();
    }

    /// <summary>The canonical names of every key in the vocabulary.</summary>
    public static IEnumerable<string> CanonicalKeyNames => NamesByKeyCode.Values;

    /// <summary>
    /// Resolves a key name to its key code, reporting whether it is in the vocabulary. It never
    /// throws: an unrecognised name is a hand-edited settings file, not a defect.
    /// </summary>
    public static bool TryParseKey(string? name, out KeyCode keyCode)
    {
        if (name is null)
        {
            keyCode = KeyCode.VcUndefined;
            return false;
        }

        return KeysByName.TryGetValue(name, out keyCode);
    }

    /// <summary>
    /// Resolves a modifier name to its group, reporting whether it is in the vocabulary. The five
    /// spellings of the command key all resolve to <see cref="HotkeyModifiers.Meta"/>, so a settings
    /// file moved between a Mac and a PC keeps working.
    /// </summary>
    public static bool TryParseModifier(string? name, out HotkeyModifiers modifier)
    {
        if (name is null)
        {
            modifier = HotkeyModifiers.None;
            return false;
        }

        return ModifiersByName.TryGetValue(name, out modifier);
    }

    /// <summary>
    /// Names the modifier groups for a binding written back into settings, in the order the
    /// defaults use, so a captured default binding produces the same file the defaults would.
    /// </summary>
    /// <remarks>
    /// The command key is named for the platform — <c>Cmd</c> on macOS, <c>Win</c> elsewhere —
    /// because that is what <see cref="Settings.HotkeyBinding"/>'s defaults use and what a user
    /// reading the file expects. All five spellings parse everywhere regardless.
    /// </remarks>
    public static IReadOnlyList<string> GetModifierNames(HotkeyModifiers modifiers)
    {
        var names = new List<string>(4);

        if (modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            names.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            names.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Meta))
        {
            names.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            names.Add("Shift");
        }

        return names;
    }

    /// <summary>
    /// Resolves a key code to the one name that can be written into settings for it, reporting
    /// false for a key outside the vocabulary rather than inventing a spelling.
    /// </summary>
    public static bool TryGetKeyName(KeyCode keyCode, out string name)
    {
        if (NamesByKeyCode.TryGetValue(keyCode, out var found))
        {
            name = found;
            return true;
        }

        name = string.Empty;
        return false;
    }
}
