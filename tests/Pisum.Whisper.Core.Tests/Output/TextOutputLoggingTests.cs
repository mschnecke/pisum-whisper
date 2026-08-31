namespace Pisum.Whisper.Core.Tests.Output;

using SharpHook.Data;
using Shouldly;

/// <summary>
/// Task 2.9 — what a delivery is allowed to write down.
/// </summary>
/// <remarks>
/// Change 3's rule is that a transcript never reaches the log; the clipboard contents this capability
/// reads are the same class of data and worse, because a password manager's clipboard is the obvious
/// thing to find there. Change 10 puts an "Open Log Folder" button a click away, so these assertions
/// are load-bearing rather than decorative.
/// </remarks>
[UnitTest]
public sealed class TextOutputLoggingTests : TextOutputTestBase
{
    private const string Password = "correct-horse-battery-staple";

    [Fact]
    public async Task AFullDelivery_LogsNeitherTheTranscriptNorThePreviousContents()
    {
        Clipboard.Text = Password;

        await Create().DeliverAsync($"  {Transcript}\n", CancellationToken.None);

        AssertNothingSensitiveWasLogged();
    }

    [Fact]
    public async Task TheCharacterCountAndTheOutcomeAreLogged()
    {
        await Create().DeliverAsync(Transcript, CancellationToken.None);

        LogMessages.ShouldContain(message => message.Contains($"{Transcript.Length}", StringComparison.Ordinal));
        LogMessages.ShouldContain(message => message.Contains("Pasted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedPasteIsLoggedWithItsResult()
    {
        Provider.PostEventResult = UioHookResult.ErrorSetWindowsHookEx;

        await Create().DeliverAsync(Transcript, CancellationToken.None);

        LogMessages.ShouldContain(message => message.Contains("ErrorSetWindowsHookEx", StringComparison.Ordinal));
        LogMessages.ShouldContain(message => message.Contains("ClipboardOnly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASkippedRestoreSaysWhyItWasSkipped_WithoutNamingWhatWasOnTheClipboard()
    {
        Clipboard.Text = Password;
        var output = Create(restoreDelay: TimeSpan.FromMilliseconds(200));

        var delivery = output.DeliverAsync(Transcript, CancellationToken.None);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Clipboard.Text = "something the user copied mid-dictation";
        await delivery;

        LogMessages.ShouldContain(message => message.Contains("restore was skipped", StringComparison.Ordinal));
        AssertNothingSensitiveWasLogged();
    }

    private void AssertNothingSensitiveWasLogged()
    {
        foreach (var logEvent in LogEvents)
        {
            logEvent.RenderMessage().ShouldNotContain(Transcript, Case.Insensitive);
            logEvent.RenderMessage().ShouldNotContain(Password, Case.Insensitive);

            // Past the rendered message as well: a property that is not in the template renders
            // nowhere and would still reach a structured sink.
            foreach (var property in logEvent.Properties.Values)
            {
                property.ToString().ShouldNotContain(Transcript, Case.Insensitive);
                property.ToString().ShouldNotContain(Password, Case.Insensitive);
            }
        }
    }
}
