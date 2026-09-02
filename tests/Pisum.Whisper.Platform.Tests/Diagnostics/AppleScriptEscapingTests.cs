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
}
