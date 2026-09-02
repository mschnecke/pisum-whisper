namespace Pisum.Whisper.Platform.Diagnostics;

/// <summary>
/// Escaping for the one AppleScript this application runs.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="MacOsFatalErrorReporter"/> on purpose, and deliberately carrying no
/// <c>[SupportedOSPlatform]</c>: this is the one part of the macOS reporter that can be verified
/// from Windows, and a call to a macOS-only member from an unguarded test is a CA1416 error under
/// warnings-as-errors — so leaving it on the reporter would put it out of reach of the tests that
/// are its whole justification.
/// </para>
/// <para>
/// Getting it wrong fails silently and totally: an unescaped quote, backslash or <b>line break</b>
/// makes the script a syntax error, <c>osascript</c> exits without drawing anything, and the user
/// sees nothing — which is precisely the condition this capability exists to prevent. All three
/// arrive in practice: <c>StartupFailure.Describe</c> passes exception messages through, and those
/// quote file names and carry Windows paths, while <b>every</b> message it produces ends with a
/// blank line and the log path.
/// </para>
/// </remarks>
internal static class AppleScript
{
    /// <summary>Escapes <paramref name="value"/> into an AppleScript string literal's contents.</summary>
    /// <remarks>
    /// <para>
    /// The backslash is replaced first. The other order would escape the backslashes introduced by
    /// escaping the quotes, and the literal would close early.
    /// </para>
    /// <para>
    /// <b>The line breaks are not optional.</b> An AppleScript string literal cannot span lines — a
    /// raw line break ends the statement — and the whole script is passed as one <c>-e</c> argument,
    /// so a message containing one is a syntax error rather than a two-line dialog. Since
    /// <c>StartupFailure.Describe</c> always appends the log path after a blank line, that is every
    /// message this ever receives. They are replaced <em>after</em> the backslash pass so the
    /// backslash each one introduces is AppleScript's own <c>\n</c> escape and is not doubled into a
    /// literal one; CRLF is matched before the single characters so a Windows line ending does not
    /// become two blank lines.
    /// </para>
    /// </remarks>
    internal static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
