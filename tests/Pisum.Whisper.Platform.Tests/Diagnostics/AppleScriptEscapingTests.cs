namespace Pisum.Whisper.Platform.Tests.Diagnostics;

using Pisum.Whisper.Platform.Diagnostics;
using Shouldly;

/// <summary>
/// Task 2.2 — the one part of the macOS reporter that can be verified from Windows.
/// </summary>
/// <remarks>
/// It is worth its own class because the failure mode is silent and total: an unescaped quote or
/// backslash makes the script a syntax error, <c>osascript</c> exits without drawing anything, and
/// the user sees nothing — which is exactly the condition this capability exists to prevent. Both
/// characters reach it in practice, because <c>StartupFailure.Describe</c> passes exception messages
/// through and those quote file names and carry Windows paths.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AppleScriptEscapingTests
{
    [Fact]
    public void APlainStringPassesThroughUnchanged()
    {
        AppleScript.Escape("Pisum Whisper could not start.")
            .ShouldBe("Pisum Whisper could not start.");
    }

    [Fact]
    public void AnEmbeddedQuoteIsEscaped()
    {
        AppleScript.Escape(@"The settings file ""a.json"" is wrong.")
            .ShouldBe(@"The settings file \""a.json\"" is wrong.");
    }

    [Fact]
    public void AnEmbeddedBackslashIsEscaped()
    {
        AppleScript.Escape(@"C:\Users\someone\.pisum-whisper.json")
            .ShouldBe(@"C:\\Users\\someone\\.pisum-whisper.json");
    }

    [Fact]
    public void BothTogetherAreEscapedOnce()
    {
        // The backslash has to go first. The other order would escape the backslashes introduced by
        // escaping the quotes, and the literal would close early.
        AppleScript.Escape(@"say ""C:\a"" now")
            .ShouldBe(@"say \""C:\\a\"" now");
    }

    [Fact]
    public void ATrailingBackslashDoesNotSwallowTheClosingQuote()
    {
        // The literal is built as "<escaped>", so a value ending in a backslash — a bare directory
        // path — is the case that would run the string on into the rest of the script.
        AppleScript.Escape(@"C:\logs\").ShouldBe(@"C:\\logs\\");
    }

    /// <summary>
    /// A raw line break ends an AppleScript statement, and the whole script is one <c>-e</c>
    /// argument — so an unescaped one is a syntax error and no dialog at all, which is the exact
    /// failure this function exists to prevent.
    /// </summary>
    /// <remarks>
    /// This is not a hypothetical input. <c>StartupFailure.Describe</c> ends <b>every</b> message it
    /// produces with a blank line and the log path, so without this the macOS dialog would never
    /// appear for any startup failure.
    /// </remarks>
    [Fact]
    public void ALineBreakBecomesAnAppleScriptEscapeRatherThanEndingTheStatement()
    {
        AppleScript.Escape("could not start.\n\nThe log is at /tmp/x.log.")
            .ShouldBe(@"could not start.\n\nThe log is at /tmp/x.log.");
    }

    [Fact]
    public void AWindowsLineEndingBecomesOneEscapeRatherThanTwo()
    {
        AppleScript.Escape("first\r\nsecond").ShouldBe(@"first\nsecond");
    }

    [Fact]
    public void ALoneCarriageReturnIsEscapedToo()
    {
        AppleScript.Escape("first\rsecond").ShouldBe(@"first\nsecond");
    }

    /// <summary>
    /// The ordering guard: the backslash pass runs first, so the backslash a line break introduces
    /// is AppleScript's own escape and is not doubled — while a literal backslash followed by the
    /// letter n stays literal text and does not become a line break.
    /// </summary>
    [Fact]
    public void ALiteralBackslashNIsNotConfusedWithALineBreak()
    {
        AppleScript.Escape(@"C:\new\thing").ShouldBe(@"C:\\new\\thing");
        AppleScript.Escape("a\nb").ShouldBe(@"a\nb");
    }
}
