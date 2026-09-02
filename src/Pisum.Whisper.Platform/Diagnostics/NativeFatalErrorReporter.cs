namespace Pisum.Whisper.Platform.Diagnostics;

using Pisum.Whisper.Core.Diagnostics;

/// <summary>
/// Constructs the fatal-error reporter for the running platform.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is registered in the container, and nothing here may be.</b> One of the four
/// call sites is <c>builder.Build()</c> failing, so a reporter resolved from the container is a
/// reporter that does not exist exactly when it is needed. <c>Program</c> constructs one on its
/// first line, in the same shape as <c>AddFileLogging(out var logger)</c> handing it a logger before
/// the container exists.
/// </para>
/// <para>
/// <b>An unsupported platform falls back to a no-op rather than throwing</b>, which diverges from
/// <c>AddNativeOutput</c> and <c>AddNativeAutostart</c> on purpose: this is the thing that reports
/// startup failing, so it must not be a startup failure itself.
/// </para>
/// </remarks>
public static class NativeFatalErrorReporter
{
    public static IFatalErrorReporter Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsFatalErrorReporter();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsFatalErrorReporter();
        }

        return new SilentFatalErrorReporter();
    }

    /// <summary>
    /// Neither shipped platform. The failure still reaches the log and still ends the process with a
    /// non-zero exit code; only the dialog is missing.
    /// </summary>
    internal sealed class SilentFatalErrorReporter : IFatalErrorReporter
    {
        public void Report(string title, string message)
        {
        }
    }
}
