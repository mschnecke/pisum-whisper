namespace Pisum.Whisper.Platform.Tests;

/// <summary>
/// The gate on the four tests that need real hardware, a real desktop session or a real API key.
/// They stay skipped — with their reason in the runner output — until the environment variable is
/// set, because xUnit has no way to run a test that is skipped unconditionally: <c>Skip</c> alone
/// would leave them unrunnable rather than merely unrun, and they exist to be run by hand on both
/// Windows and macOS.
/// </summary>
internal static class ManualTests
{
    /// <summary>Set <c>PISUM_WHISPER_RUN_MANUAL</c> to anything non-empty to opt in.</summary>
    public static bool Enabled =>
        Environment.GetEnvironmentVariable("PISUM_WHISPER_RUN_MANUAL") is not (null or "");
}
