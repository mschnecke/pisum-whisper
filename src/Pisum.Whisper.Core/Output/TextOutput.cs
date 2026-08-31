namespace Pisum.Whisper.Core.Output;

using Microsoft.Extensions.Logging;
using SharpHook.Data;
using SharpHook.Simulation;

/// <summary>
/// The whole delivery sequence: read the clipboard's previous text, write the transcript, paste it
/// into whichever application holds focus, then put the previous text back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neither the transcript nor the clipboard's previous contents are ever logged</b>, at any level.
/// The transcript is the user's speech and a clipboard is as likely as not to hold a password. What
/// may be written down is the transcript's character count, the outcome, and which guard stood a
/// restore down; the previous contents are not logged even by length.
/// </para>
/// <para>
/// <b>Never call this from a hook handler.</b> It sleeps for more than a second, and both platforms
/// police that thread — Windows silently removes a low-level hook that exceeds
/// <c>LowLevelHooksTimeout</c>.
/// </para>
/// </remarks>
public sealed class TextOutput : ITextOutput
{
    /// <summary>The reference's constant: long enough for the clipboard write to settle before the paste.</summary>
    private static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// How long the transcript is left on the clipboard for the focused application to read it.
    /// Nothing on either platform reports when that read happens, and the cost of being wrong is
    /// entirely one-sided: too late costs a second of an invisible tail, while too early makes the
    /// target paste the previous clipboard contents — as likely as not a password — into the user's
    /// document. So the delay is generous.
    /// </summary>
    private static readonly TimeSpan DefaultRestoreDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The pause after every simulated edge on macOS. Change 1's spike found that edges posted back
    /// to back outrun the operating system folding earlier keys into the modifier flags, so Cmd+V
    /// arrives as a bare "v". Windows needs no pacing and gets none.
    /// </summary>
    private static readonly TimeSpan MacOsEdgePause = TimeSpan.FromMilliseconds(30);

    private readonly ILogger<TextOutput> _logger;

    private readonly ISystemClipboard _clipboard;

    private readonly IPasteProbe _probe;

    private readonly IEventSimulator _simulator;

    private readonly TimeSpan _settleDelay;

    private readonly TimeSpan _restoreDelay;

    private readonly bool _paceEdges;

    private readonly KeyCode _modifier;

    /// <summary>
    /// Held across the whole sequence. Two deliveries in flight at once defeat the guards rather
    /// than tripping them: the second reads the first one's transcript, takes it for the user's
    /// clipboard, and later restores a transcript over contents that by then are held nowhere at all.
    /// No guard can see that, because from the second delivery's position it is indistinguishable
    /// from the user having copied something.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TextOutput(ILogger<TextOutput> logger,
                      ISystemClipboard clipboard,
                      IPasteProbe probe,
                      IEventSimulator simulator)
        : this(logger, clipboard, probe, simulator, OperatingSystem.IsMacOS())
    {
    }

    /// <summary>
    /// Constructs the sequence over an explicit platform selection and explicit delays, which is how
    /// the tests assert the macOS keystroke from Windows and finish in milliseconds — the same shape
    /// as <see cref="Hotkeys.GlobalHotkeyService"/>'s test constructor.
    /// </summary>
    internal TextOutput(ILogger<TextOutput> logger,
                        ISystemClipboard clipboard,
                        IPasteProbe probe,
                        IEventSimulator simulator,
                        bool macOs,
                        TimeSpan? settleDelay = null,
                        TimeSpan? restoreDelay = null)
    {
        _logger = logger;
        _clipboard = clipboard;
        _probe = probe;
        _simulator = simulator;
        _paceEdges = macOs;
        _modifier = macOs ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;
        _settleDelay = settleDelay ?? DefaultSettleDelay;
        _restoreDelay = restoreDelay ?? DefaultRestoreDelay;
    }

    public async Task<TextOutputOutcome> DeliverAsync(string transcript, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        // Step 0. Gemini's responses routinely end in a newline and nothing upstream trims them, so
        // without this every single dictation pastes a stray blank line at the cursor. What the
        // model returns is its business; how it arrives at the insertion point is ours.
        var text = transcript.Trim();

        if (text.Length == 0)
        {
            // A programming error rather than a runtime condition: an unusable response from Gemini
            // is already an ErrorCategory.Transcription failure on change 5's side.
            throw new ArgumentException(
                "There is nothing to deliver: the text is empty once surrounding whitespace is removed.",
                nameof(transcript));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await DeliverExclusivelyAsync(text, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TextOutputOutcome> DeliverExclusivelyAsync(string text, CancellationToken cancellationToken)
    {
        // The last point at which cancelling costs nothing. Past the write below the restore is
        // owed, and abandoning it destroys the user's clipboard permanently.
        cancellationToken.ThrowIfCancellationRequested();

        var previous = ReadPreviousText();

        WriteToClipboard(text);

        var outcome = await PasteAndRestoreAsync(text, previous, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Delivered {CharacterCount} characters: {Outcome}.",
            text.Length,
            outcome);

        return outcome;
    }

    private async Task<TextOutputOutcome> PasteAndRestoreAsync(string text,
                                                               string? previous,
                                                               CancellationToken cancellationToken)
    {
        // Step 3, and guard 1's first half. A paste the platform will drop reports Success, so
        // without asking first the sequence would restore the previous contents over the transcript
        // and lose the user's speech with no message at all.
        if (!_probe.CanPaste())
        {
            _logger.LogWarning(
                "The focused application cannot be reached by synthetic input, so no paste was sent. "
                + "The text is on the clipboard and can be pasted manually.");

            return TextOutputOutcome.ClipboardOnly;
        }

        // Step 4. Not cancellable: the transcript is already on the clipboard, and abandoning here
        // would skip the restore the write just made us owe.
        await Task.Delay(_settleDelay, CancellationToken.None).ConfigureAwait(false);

        // Step 5, and guard 1's second half. The transcript stays on the clipboard, because the
        // degraded outcome tells the user to press the paste combination themselves and restoring
        // over it would make that a lie.
        var result = await SendPasteAsync().ConfigureAwait(false);

        if (result != UioHookResult.Success)
        {
            _logger.LogWarning(
                "The paste keystroke could not be sent ({Result}). The text is on the clipboard and "
                + "can be pasted manually.",
                result);

            return TextOutputOutcome.ClipboardOnly;
        }

        // Step 6. Cancellation shortens this rather than skipping step 7.
        await WaitBeforeRestoreAsync(cancellationToken).ConfigureAwait(false);

        RestorePreviousText(text, previous);

        return TextOutputOutcome.Pasted;
    }

    private string? ReadPreviousText()
    {
        try
        {
            return _clipboard.TryGetText();
        }
        catch (Exception exception)
        {
            // Best effort by design: the dictation is worth more than the restore, and a clipboard
            // this application cannot read is one it can still write.
            _logger.LogWarning(
                exception,
                "The clipboard's existing contents could not be read; the delivery continues with "
                + "nothing to restore.");

            return null;
        }
    }

    private void WriteToClipboard(string text)
    {
        try
        {
            _clipboard.SetText(text);
        }
        catch (Exception exception)
        {
            // The one outcome in which the transcript is genuinely lost, so it throws rather than
            // returning a degraded value, and no keystroke follows.
            throw new TextOutputException(
                $"The text could not be placed on the clipboard: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Posts the four edges of the platform's paste combination and reports the first result that
    /// was not <see cref="UioHookResult.Success"/>.
    /// </summary>
    /// <remarks>
    /// Every edge is posted even after one fails. Stopping half way leaves the modifier held down on
    /// the user's machine, which is a worse state to be left in than a paste that did not land.
    /// </remarks>
    private async Task<UioHookResult> SendPasteAsync()
    {
        var outcome = UioHookResult.Success;

        foreach (var edge in PasteEdges())
        {
            var result = edge();

            if (outcome == UioHookResult.Success)
            {
                outcome = result;
            }

            if (_paceEdges)
            {
                await Task.Delay(MacOsEdgePause, CancellationToken.None).ConfigureAwait(false);
            }
        }

        return outcome;
    }

    private IEnumerable<Func<UioHookResult>> PasteEdges()
    {
        yield return () => _simulator.SimulateKeyPress(_modifier);
        yield return () => _simulator.SimulateKeyPress(KeyCode.VcV);
        yield return () => _simulator.SimulateKeyRelease(KeyCode.VcV);
        yield return () => _simulator.SimulateKeyRelease(_modifier);
    }

    private async Task WaitBeforeRestoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_restoreDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shortened, not abandoned. Between the write and the restore the user's clipboard text
            // exists nowhere but in this call, and on Windows the transcript outlives the process
            // that wrote it, so quitting inside this window would destroy the clipboard permanently.
            _logger.LogDebug("The delivery was cancelled, so the wait before the restore was cut short.");
        }
    }

    /// <summary>Step 7, under guards 2 and 3.</summary>
    private void RestorePreviousText(string text, string? previous)
    {
        // Guard 3. An empty clipboard, an image or a file list all read as no text, and
        // round-tripping arbitrary formats is a non-goal — the transcript simply stays.
        if (previous is null)
        {
            _logger.LogDebug("Nothing to restore: the clipboard held no text before the delivery.");
            return;
        }

        string? current;

        try
        {
            current = _clipboard.TryGetText();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The restore was skipped: the clipboard could not be read back.");
            return;
        }

        // Guard 2. Transcription takes seconds; anything the user copied in the meantime is newer
        // than what this delivery saved and wins. It is also what makes a second delivery safe —
        // its transcript is not ours, so this restore stands down.
        if (!string.Equals(current, text, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "The restore was skipped: the clipboard no longer holds this delivery's text, so its "
                + "contents are newer than what was saved.");

            return;
        }

        try
        {
            _clipboard.SetText(previous);
        }
        catch (Exception exception)
        {
            // The paste already succeeded, so this is logged rather than thrown: the user has their
            // transcript, and what they have lost is the clipboard entry that preceded it.
            _logger.LogWarning(exception, "The clipboard's previous contents could not be restored.");
        }
    }
}
