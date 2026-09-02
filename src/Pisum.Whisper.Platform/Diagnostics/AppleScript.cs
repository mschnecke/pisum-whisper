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
/// Getting it wrong fails silently and totally: an unescaped quote or backslash makes the script a
/// syntax error, <c>osascript</c> exits without drawing anything, and the user sees nothing — which
/// is precisely the condition this capability exists to prevent. Both characters arrive in practice,
/// because <c>StartupFailure.Describe</c> passes exception messages through and those quote file
/// names and carry Windows paths.
/// </para>
/// </remarks>
internal static class AppleScript
{
    /// <summary>Escapes <paramref name="value"/> into an AppleScript string literal's contents.</summary>
    /// <remarks>
    /// The backslash is replaced first. The other order would escape the backslashes introduced by
    /// escaping the quotes, and the literal would close early.
    /// </remarks>
    internal static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
