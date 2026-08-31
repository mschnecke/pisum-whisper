namespace Pisum.Whisper.Core.Tests.Hotkeys;

using Pisum.Whisper.Core.Hotkeys;
using Shouldly;

/// <summary>
/// Pins the modifier vocabulary from the reference's <c>parse_modifiers</c>. The five spellings of
/// the command key matter in practice: the settings file defaults to <c>Cmd</c> on macOS and
/// <c>Ctrl</c> elsewhere, and a file carried between the two must still load.
/// </summary>
public sealed class KeyCodeMapModifierTests
{
    private static HotkeyModifiers Parse(string name)
    {
        KeyCodeMap.TryParseModifier(name, out var modifier).ShouldBeTrue($"'{name}' should be in the vocabulary");
        return modifier;
    }

    [Fact]
    public void CtrlAndControl_ResolveToTheSameGroup()
    {
        Parse("Ctrl").ShouldBe(HotkeyModifiers.Ctrl);
        Parse("Control").ShouldBe(HotkeyModifiers.Ctrl);
    }

    [Fact]
    public void AllFiveMetaSpellings_ResolveToTheSameGroup()
    {
        Parse("Meta").ShouldBe(HotkeyModifiers.Meta);
        Parse("Super").ShouldBe(HotkeyModifiers.Meta);
        Parse("Win").ShouldBe(HotkeyModifiers.Meta);
        Parse("Cmd").ShouldBe(HotkeyModifiers.Meta);
        Parse("Command").ShouldBe(HotkeyModifiers.Meta);
    }

    [Fact]
    public void AltAndShift_ResolveToTheirOwnGroups()
    {
        Parse("Alt").ShouldBe(HotkeyModifiers.Alt);
        Parse("Shift").ShouldBe(HotkeyModifiers.Shift);
    }

    [Fact]
    public void Names_MatchRegardlessOfCase()
    {
        Parse("ctrl").ShouldBe(HotkeyModifiers.Ctrl);
        Parse("CONTROL").ShouldBe(HotkeyModifiers.Ctrl);
        Parse("cMd").ShouldBe(HotkeyModifiers.Meta);
        Parse("SHIFT").ShouldBe(HotkeyModifiers.Shift);
    }

    [Fact]
    public void UnknownModifier_ReportsFalseRatherThanThrowing()
    {
        KeyCodeMap.TryParseModifier("Hyper", out var modifier).ShouldBeFalse();
        modifier.ShouldBe(HotkeyModifiers.None);

        KeyCodeMap.TryParseModifier(string.Empty, out _).ShouldBeFalse();
        KeyCodeMap.TryParseModifier(null, out _).ShouldBeFalse();
    }

    [Fact]
    public void ModifierNames_AreNotKeyNames()
    {
        // A binding names its modifiers separately from its key. Letting "Ctrl" resolve as a key
        // would make a nonsense binding compile.
        KeyCodeMap.TryParseKey("Ctrl", out _).ShouldBeFalse();
        KeyCodeMap.TryParseKey("Shift", out _).ShouldBeFalse();
    }
}
