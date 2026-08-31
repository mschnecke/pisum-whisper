namespace Pisum.Whisper.Core.Tests.Output;

using System.Diagnostics;
using SharpHook.Data;
using Shouldly;

/// <summary>
/// Task 2.2 — the shape of the paste keystroke on each platform, asserted from either host through
/// the internal constructor's platform selection.
/// </summary>
public sealed class TextOutputKeystrokeTests : TextOutputTestBase
{
    [Fact]
    public async Task TheWindowsSelection_SendsCtrlV()
    {
        await Create(macOs: false).DeliverAsync(Transcript, CancellationToken.None);

        Posted.ShouldBe(
        [
            (EventType.KeyPressed, KeyCode.VcLeftControl),
            (EventType.KeyPressed, KeyCode.VcV),
            (EventType.KeyReleased, KeyCode.VcV),
            (EventType.KeyReleased, KeyCode.VcLeftControl),
        ]);
    }

    [Fact]
    public async Task TheMacOsSelection_SendsCmdV()
    {
        await Create(macOs: true).DeliverAsync(Transcript, CancellationToken.None);

        Posted.ShouldBe(
        [
            (EventType.KeyPressed, KeyCode.VcLeftMeta),
            (EventType.KeyPressed, KeyCode.VcV),
            (EventType.KeyReleased, KeyCode.VcV),
            (EventType.KeyReleased, KeyCode.VcLeftMeta),
        ]);
    }

    [Fact]
    public async Task TheMacOsSelection_PacesTheEdgesApartAndTheWindowsOneDoesNot()
    {
        // The pacing is why this is not an implementation detail: change 1's spike found that edges
        // posted back to back outrun macOS folding earlier keys into the modifier flags, so Cmd+V
        // arrives as a bare "v" and the paste silently becomes a typo in the user's document.
        var windows = await TimeDeliveryAsync(macOs: false);
        var mac = await TimeDeliveryAsync(macOs: true);

        mac.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(100));
        windows.ShouldBeLessThan(TimeSpan.FromMilliseconds(100));
    }

    private async Task<TimeSpan> TimeDeliveryAsync(bool macOs)
    {
        // No restore wait, so what is measured is the keystroke and nothing else.
        var output = Create(macOs, restoreDelay: TimeSpan.Zero);

        var stopwatch = Stopwatch.StartNew();
        await output.DeliverAsync(Transcript, CancellationToken.None);
        return stopwatch.Elapsed;
    }
}
