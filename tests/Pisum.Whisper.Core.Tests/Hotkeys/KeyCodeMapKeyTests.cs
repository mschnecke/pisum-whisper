namespace Pisum.Whisper.Core.Tests.Hotkeys;

using Pisum.Whisper.Core.Hotkeys;
using SharpHook.Data;
using Shouldly;

/// <summary>
/// Pins the forward key vocabulary against the reference's <c>hotkey/parse.rs</c> table. A settings
/// file written for the reference must still load here, so every row it accepts is asserted.
/// </summary>
[UnitTest]
public sealed class KeyCodeMapKeyTests
{
    private static KeyCode Parse(string name)
    {
        KeyCodeMap.TryParseKey(name, out var keyCode).ShouldBeTrue($"'{name}' should be in the vocabulary");
        return keyCode;
    }

    [Fact]
    public void Letters_CoverAToZ()
    {
        Parse("A").ShouldBe(KeyCode.VcA);
        Parse("M").ShouldBe(KeyCode.VcM);
        Parse("Z").ShouldBe(KeyCode.VcZ);

        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            KeyCodeMap.TryParseKey(letter.ToString(), out _).ShouldBeTrue();
        }
    }

    [Fact]
    public void Digits_CoverZeroToNine_UnderBothSpellings()
    {
        Parse("0").ShouldBe(KeyCode.Vc0);
        Parse("9").ShouldBe(KeyCode.Vc9);
        Parse("Digit0").ShouldBe(KeyCode.Vc0);
        Parse("Digit7").ShouldBe(KeyCode.Vc7);
    }

    [Fact]
    public void FunctionKeys_CoverF1ToF12()
    {
        Parse("F1").ShouldBe(KeyCode.VcF1);
        Parse("F9").ShouldBe(KeyCode.VcF9);
        Parse("F12").ShouldBe(KeyCode.VcF12);
    }

    [Fact]
    public void FunctionKeys_StopAtF12()
    {
        // The reference's table ends at F12. F13 upwards exist in SharpHook and are deliberately
        // outside the vocabulary; design.md records closing that gap as change 10's trigger.
        KeyCodeMap.TryParseKey("F13", out _).ShouldBeFalse();
    }

    [Fact]
    public void SpecialKeys_ResolveUnderTheirFullNames()
    {
        Parse("Space").ShouldBe(KeyCode.VcSpace);
        Parse("Enter").ShouldBe(KeyCode.VcEnter);
        Parse("Tab").ShouldBe(KeyCode.VcTab);
        Parse("Escape").ShouldBe(KeyCode.VcEscape);
        Parse("Backspace").ShouldBe(KeyCode.VcBackspace);
        Parse("Delete").ShouldBe(KeyCode.VcDelete);
        Parse("Insert").ShouldBe(KeyCode.VcInsert);
        Parse("Home").ShouldBe(KeyCode.VcHome);
        Parse("End").ShouldBe(KeyCode.VcEnd);
        Parse("PageUp").ShouldBe(KeyCode.VcPageUp);
        Parse("PageDown").ShouldBe(KeyCode.VcPageDown);
    }

    [Fact]
    public void SpecialKeys_ResolveUnderTheReferenceAliases()
    {
        Parse(" ").ShouldBe(KeyCode.VcSpace);
        Parse("Return").ShouldBe(KeyCode.VcEnter);
        Parse("Esc").ShouldBe(KeyCode.VcEscape);
        Parse("Del").ShouldBe(KeyCode.VcDelete);
        Parse("Ins").ShouldBe(KeyCode.VcInsert);
        Parse("PgUp").ShouldBe(KeyCode.VcPageUp);
        Parse("PgDn").ShouldBe(KeyCode.VcPageDown);
    }

    [Fact]
    public void Arrows_ResolveUnderBothSpellings()
    {
        Parse("Up").ShouldBe(KeyCode.VcUp);
        Parse("Down").ShouldBe(KeyCode.VcDown);
        Parse("Left").ShouldBe(KeyCode.VcLeft);
        Parse("Right").ShouldBe(KeyCode.VcRight);
        Parse("ArrowUp").ShouldBe(KeyCode.VcUp);
        Parse("ArrowDown").ShouldBe(KeyCode.VcDown);
        Parse("ArrowLeft").ShouldBe(KeyCode.VcLeft);
        Parse("ArrowRight").ShouldBe(KeyCode.VcRight);
    }

    [Fact]
    public void Punctuation_ResolvesUnderNameAndCharacter()
    {
        Parse("Minus").ShouldBe(KeyCode.VcMinus);
        Parse("-").ShouldBe(KeyCode.VcMinus);
        Parse("Equal").ShouldBe(KeyCode.VcEquals);
        Parse("=").ShouldBe(KeyCode.VcEquals);
        Parse("BracketLeft").ShouldBe(KeyCode.VcOpenBracket);
        Parse("[").ShouldBe(KeyCode.VcOpenBracket);
        Parse("BracketRight").ShouldBe(KeyCode.VcCloseBracket);
        Parse("]").ShouldBe(KeyCode.VcCloseBracket);
        Parse("Backslash").ShouldBe(KeyCode.VcBackslash);
        Parse("\\").ShouldBe(KeyCode.VcBackslash);
        Parse("Semicolon").ShouldBe(KeyCode.VcSemicolon);
        Parse(";").ShouldBe(KeyCode.VcSemicolon);
        Parse("Quote").ShouldBe(KeyCode.VcQuote);
        Parse("'").ShouldBe(KeyCode.VcQuote);
        Parse("BackQuote").ShouldBe(KeyCode.VcBackQuote);
        Parse("`").ShouldBe(KeyCode.VcBackQuote);
        Parse("Comma").ShouldBe(KeyCode.VcComma);
        Parse(",").ShouldBe(KeyCode.VcComma);
        Parse("Period").ShouldBe(KeyCode.VcPeriod);
        Parse(".").ShouldBe(KeyCode.VcPeriod);
        Parse("Slash").ShouldBe(KeyCode.VcSlash);
        Parse("/").ShouldBe(KeyCode.VcSlash);
    }

    [Fact]
    public void Numpad_ResolvesDigitsAndOperators()
    {
        Parse("Numpad0").ShouldBe(KeyCode.VcNumPad0);
        Parse("Numpad9").ShouldBe(KeyCode.VcNumPad9);
        Parse("NumpadAdd").ShouldBe(KeyCode.VcNumPadAdd);
        Parse("Numpad+").ShouldBe(KeyCode.VcNumPadAdd);
        Parse("NumpadSubtract").ShouldBe(KeyCode.VcNumPadSubtract);
        Parse("Numpad-").ShouldBe(KeyCode.VcNumPadSubtract);
        Parse("NumpadMultiply").ShouldBe(KeyCode.VcNumPadMultiply);
        Parse("Numpad*").ShouldBe(KeyCode.VcNumPadMultiply);
        Parse("NumpadDivide").ShouldBe(KeyCode.VcNumPadDivide);
        Parse("Numpad/").ShouldBe(KeyCode.VcNumPadDivide);
        Parse("NumpadDecimal").ShouldBe(KeyCode.VcNumPadDecimal);
        Parse("Numpad.").ShouldBe(KeyCode.VcNumPadDecimal);
        Parse("NumpadEnter").ShouldBe(KeyCode.VcNumPadEnter);
    }

    [Fact]
    public void Names_MatchRegardlessOfCase()
    {
        Parse("space").ShouldBe(KeyCode.VcSpace);
        Parse("SPACE").ShouldBe(KeyCode.VcSpace);
        Parse("SpAcE").ShouldBe(KeyCode.VcSpace);
        Parse("pageup").ShouldBe(KeyCode.VcPageUp);
        Parse("NUMPADADD").ShouldBe(KeyCode.VcNumPadAdd);
        Parse("a").ShouldBe(KeyCode.VcA);
    }

    [Fact]
    public void UnknownName_ReportsFalseRatherThanThrowing()
    {
        KeyCodeMap.TryParseKey("Nonsense", out var keyCode).ShouldBeFalse();
        keyCode.ShouldBe(KeyCode.VcUndefined);

        KeyCodeMap.TryParseKey(string.Empty, out _).ShouldBeFalse();
        KeyCodeMap.TryParseKey(null, out _).ShouldBeFalse();
    }
}
