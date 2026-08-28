namespace Pisum.Whisper.Core.Tests.Hotkeys;

using Pisum.Whisper.Core.Hotkeys;
using SharpHook.Data;
using Shouldly;

/// <summary>
/// The reverse direction is what change 10's hotkey recorder writes back into settings, so a name
/// it produces must parse to the key it came from. A key with no name must say so rather than
/// return a spelling nothing else understands.
/// </summary>
[TestClass]
public sealed class KeyCodeMapReverseTests
{
    [TestMethod]
    public void EveryCanonicalName_RoundTripsToItsOwnKeyCode()
    {
        var names = KeyCodeMap.CanonicalKeyNames.ToList();
        names.ShouldNotBeEmpty();

        foreach (var name in names)
        {
            KeyCodeMap.TryParseKey(name, out var keyCode)
                .ShouldBeTrue($"canonical name '{name}' should parse");

            KeyCodeMap.TryGetKeyName(keyCode, out var roundTripped)
                .ShouldBeTrue($"'{name}' parsed to {keyCode}, which should have a name");

            roundTripped.ShouldBe(name);
        }
    }

    [TestMethod]
    public void CanonicalNames_AreTheSpellingsTheSettingsFileAlreadyUses()
    {
        // The default binding change 2 writes is {"modifiers":["Ctrl","Shift"],"key":"Space"}.
        // A captured binding must be written in the same spelling, not a shouted variant.
        KeyCodeMap.TryGetKeyName(KeyCode.VcSpace, out var space).ShouldBeTrue();
        space.ShouldBe("Space");

        KeyCodeMap.TryGetKeyName(KeyCode.VcPageUp, out var pageUp).ShouldBeTrue();
        pageUp.ShouldBe("PageUp");
    }

    [TestMethod]
    public void AliasedKeys_ResolveToTheirPrimaryName()
    {
        KeyCodeMap.TryParseKey("Esc", out var escape).ShouldBeTrue();
        KeyCodeMap.TryGetKeyName(escape, out var name).ShouldBeTrue();
        name.ShouldBe("Escape");

        KeyCodeMap.TryParseKey("ArrowUp", out var up).ShouldBeTrue();
        KeyCodeMap.TryGetKeyName(up, out var upName).ShouldBeTrue();
        upName.ShouldBe("Up");

        KeyCodeMap.TryParseKey("-", out var minus).ShouldBeTrue();
        KeyCodeMap.TryGetKeyName(minus, out var minusName).ShouldBeTrue();
        minusName.ShouldBe("Minus");
    }

    [TestMethod]
    public void DigitAndNumpadKeys_KeepDistinctNames()
    {
        KeyCodeMap.TryGetKeyName(KeyCode.Vc1, out var digit).ShouldBeTrue();
        digit.ShouldBe("1");

        KeyCodeMap.TryGetKeyName(KeyCode.VcNumPad1, out var numpad).ShouldBeTrue();
        numpad.ShouldBe("Numpad1");
    }

    [TestMethod]
    public void KeyOutsideTheVocabulary_ReportsNoName()
    {
        KeyCodeMap.TryGetKeyName(KeyCode.VcF13, out var name).ShouldBeFalse();
        name.ShouldBeEmpty();

        KeyCodeMap.TryGetKeyName(KeyCode.VcPrintScreen, out _).ShouldBeFalse();
        KeyCodeMap.TryGetKeyName(KeyCode.VcUndefined, out _).ShouldBeFalse();
    }

    [TestMethod]
    public void ModifierKeys_HaveNoKeyName()
    {
        // A modifier is not a main key. Naming one here would let a recorder persist a binding
        // whose key is Shift.
        KeyCodeMap.TryGetKeyName(KeyCode.VcLeftShift, out _).ShouldBeFalse();
        KeyCodeMap.TryGetKeyName(KeyCode.VcRightControl, out _).ShouldBeFalse();
        KeyCodeMap.TryGetKeyName(KeyCode.VcLeftMeta, out _).ShouldBeFalse();
    }
}
