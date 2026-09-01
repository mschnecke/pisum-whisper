namespace Pisum.Whisper.Platform.Tests;

/// <summary>
/// The gate on tests that reach a mechanism only Windows has — the registry, today. They report
/// skipped with their reason on macOS, in the manner of <see cref="ManualTests"/>, rather than being
/// absent from the run.
/// </summary>
/// <remarks>
/// Its macOS counterpart is deliberately absent: <c>MacOsAutostart</c> writes a plist into an
/// injected directory and touches nothing AppKit, so its round trip runs on any operating system and
/// only the effect on login needs hardware.
/// </remarks>
internal static class WindowsOnly
{
    public static bool Enabled => OperatingSystem.IsWindows();
}
