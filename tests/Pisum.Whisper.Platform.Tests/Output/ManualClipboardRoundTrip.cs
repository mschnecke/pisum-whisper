namespace Pisum.Whisper.Platform.Tests.Output;

using Microsoft.Extensions.DependencyInjection;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Platform.Output;
using Shouldly;
using Pisum.Whisper.Platform.Tests;

/// <summary>
/// Task 5.1 — the only thing that exercises the native clipboard against a real one, in the role
/// <c>ManualCaptureSmokeTest</c> plays for the microphone and <c>ManualTranscriptionSmokeTest</c>
/// for Gemini. The sequence logic is tested in <c>Core.Tests</c> with no clipboard at all; what is
/// left here is the interop, and a real clipboard is not something a CI agent reliably has.
/// </summary>
/// <remarks>
/// Run it by name on a desktop session. It puts a token on the clipboard, reads it back, and puts
/// whatever was there before back afterwards — so running it does not cost you what you had copied.
/// Task 3.2's retry is exercised by running it while a second process holds the clipboard, for
/// example a PowerShell loop calling <c>Set-Clipboard</c>.
/// </remarks>
public sealed class ManualClipboardRoundTrip
{
    [Fact(
        Skip = "Requires a real desktop clipboard; run manually",
        SkipUnless = nameof(ManualTests.Enabled),
        SkipType = typeof(ManualTests))]
    public void ATokenSurvivesAWriteAndAReadBack()
    {
        using var provider = new ServiceCollection().AddNativeOutput().BuildServiceProvider();
        var clipboard = provider.GetRequiredService<ISystemClipboard>();

        var previous = clipboard.TryGetText();
        var token = $"PISUM-ROUNDTRIP-{Guid.NewGuid():N}";

        try
        {
            clipboard.SetText(token);

            clipboard.TryGetText().ShouldBe(token);
        }
        finally
        {
            if (previous is not null)
            {
                clipboard.SetText(previous);
            }
        }

        Console.WriteLine(
            previous is null
                ? "The clipboard held no text before this test, so nothing was restored."
                : "The clipboard's previous text was restored.");
    }
}
