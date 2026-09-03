namespace Pisum.Whisper.Core.Tests;

/// <summary>
/// The gate on tests that measure the machine rather than the code. One test carries it today:
/// <c>FileLoggingRotationTests.WritesDoNotStallTheCallingThreadWhenTheFileRolls</c>, whose p99.9
/// write latency bound of 500 us means something on a quiet developer machine and nothing on a
/// shared CI runner.
/// </summary>
/// <remarks>
/// <para>
/// It is a skip rather than a <c>--filter-not-method</c> in the CI invocation, and that is the
/// point: a skip prints its reason in the runner output beside the test, where a name in a workflow
/// file is invisible to everyone reading the run. The continuous-integration command then needs
/// exactly one filter, the Manual trait.
/// </para>
/// <para>
/// The shape is <see cref="ManualTests"/>' and <c>WindowsOnly</c>'s, for the reason
/// <see cref="ManualTests"/> gives: xUnit has no way to run a test that is skipped unconditionally,
/// so <c>Skip</c> alone would leave this unrunnable rather than merely unrun. The bound it guards is
/// <c>file-logging</c>'s to set, and gating it is not a decision that it is wrong.
/// </para>
/// </remarks>
internal static class TimingTests
{
    /// <summary>Set <c>PISUM_WHISPER_RUN_TIMING</c> to anything non-empty to opt in.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("PISUM_WHISPER_RUN_TIMING") is not (null or "");
}
