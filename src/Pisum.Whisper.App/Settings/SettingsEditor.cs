namespace Pisum.Whisper.App.Settings;

using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Notifications;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// Applies a settings edit to a private draft and writes it once the user stops typing.
/// </summary>
/// <remarks>
/// <para>
/// The window has no OK, Cancel, or Apply, so every edit persists by itself. Writing on each
/// keystroke — which the reference does — would serialize the file and raise
/// <see cref="SettingsStore.Changed"/> once per character, and would make a half-typed API key
/// visible to a dictation already in flight, because the provider pool reads
/// <see cref="SettingsStore.Current"/> at transcribe time. So edits land on a clone, and the clone is
/// saved after <see cref="CommitDelay"/> of quiet.
/// </para>
/// <para>
/// <see cref="Edit"/> is called on the UI thread; the commit runs on a pooled thread when the delay
/// completes. That is safe because the commit touches only the store, and the two
/// <see cref="SettingsStore.Changed"/> subscribers that could care marshal through the dispatcher
/// themselves.
/// </para>
/// </remarks>
public sealed class SettingsEditor
{
    /// <summary>
    /// How long the editor waits for the typing to stop before writing. A typing pause: long enough
    /// that continuous typing coalesces into one writing, short enough that a deliberate pause commits
    /// before the user can reach the window's close button. A constant, deliberately not a setting.
    /// </summary>
    internal static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(400);

    private const string SaveFailureTitle = "Settings Not Saved";

    private readonly SettingsStore _store;

    private readonly ILogger<SettingsEditor> _logger;

    private readonly INotificationService _notifications;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly Lock _gate = new();

    private AppSettings? _pending;

    private int _pendingEdits;

    private CancellationTokenSource? _quiet;

    /// <summary>The commit currently in flight or scheduled, awaited by <see cref="FlushAsync"/>.</summary>
    private Task _commit = Task.CompletedTask;

    public SettingsEditor(SettingsStore store, ILogger<SettingsEditor> logger, INotificationService notifications)
        : this(store, logger, notifications, null)
    {
    }

    /// <summary>
    /// Takes the delay as a delegate so the tests do not spend 400 ms of real time per edit,
    /// following <c>GlobalHotkeyService</c> and <c>DictationOrchestrator</c>.
    /// </summary>
    internal SettingsEditor(SettingsStore store,
                            ILogger<SettingsEditor> logger,
                            INotificationService notifications,
                            Func<TimeSpan, CancellationToken, Task>? delay)
    {
        _store = store;
        _logger = logger;
        _notifications = notifications;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>
    /// Applies <paramref name="edit"/> to the pending draft and restarts the quiet window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An edit locates its target inside the <see cref="AppSettings"/> it is handed, by
    /// <c>Id</c>, and never through a reference captured from an earlier
    /// <see cref="SettingsStore.Current"/>.</b> <see cref="SettingsStore.Save"/> assigns
    /// <c>Current = draft</c>, so the graph is replaced on every commit; a <c>ProviderConfig</c> or
    /// <c>Preset</c> captured before one belongs to a graph nothing will ever save, and the edit
    /// vanishes with no exception to say so.
    /// </para>
    /// <para>
    /// Look the target up with <c>FirstOrDefault</c> and return when the id is gone, never with
    /// <c>First</c>: a removal and a keystroke can land in the same quiet window, and a <c>First</c>
    /// here throws on the pooled commit thread as an unobserved task exception.
    /// </para>
    /// </remarks>
    public void Edit(Action<AppSettings> edit)
    {
        lock (_gate)
        {
            // Taken at the start of each quiet window rather than once per editor lifetime: a clone
            // held across the session would be saved over anything written to the store by another
            // route, which the Presets tab does on every one of its commands.
            _pending ??= _store.CloneCurrent();
            edit(_pending);
            _pendingEdits++;

            var superseded = _quiet;

            // The new window is published *before* the old one is canceled, and the order is not
            // cosmetic. Cancelling runs the waiting continuation inline on this thread, and the lock
            // is reentrant, so a superseded task reaches its ownership check while this call is
            // still inside the lock. Assigning first is what lets it find that it no longer owns
            // _quiet; cancelling first left it looking at itself and committing every keystroke.
            var quiet = new CancellationTokenSource();
            _quiet = quiet;
            _commit = CommitAfterQuietAsync(quiet);

            superseded?.Cancel();
            superseded?.Dispose();
        }
    }

    /// <summary>
    /// Writes a pending draft now rather than waiting out the quiet window and completes once it is
    /// written. A no-op when nothing is pending and safe to call twice.
    /// </summary>
    public async Task FlushAsync()
    {
        CancellationTokenSource? quiet;
        Task commit;

        lock (_gate)
        {
            quiet = _quiet;
            commit = _commit;
        }

        // Outside the lock, because cancelling runs the scheduled commit inline: it would otherwise
        // save and raise Changed into its subscribers, while this call still held the gate.
        // Cancelling brings that commit forward rather than adding a second one, so the two cannot
        // both write - the draft is claimed once, under the lock, whoever gets there first.
        quiet?.Cancel();

        await commit.ConfigureAwait(false);
    }

    /// <summary>
    /// Waits out one quiet window and commits what is pending, unless a later <see cref="Edit"/> has
    /// opened a window of its own in the meantime.
    /// </summary>
    /// <remarks>
    /// The cancellation has two causes, and they mean opposite things. <see cref="Edit"/> cancels to
    /// restart the window, and this task must then do nothing, because the call that canceled it
    /// owns the commit now. <see cref="FlushAsync"/> cancels to bring the commit forward, and this
    /// task must write. They are told apart by ownership: only the source still held in
    /// <see cref="_quiet"/> may commit.
    /// </remarks>
    private async Task CommitAfterQuietAsync(CancellationTokenSource quiet)
    {
        try
        {
            await _delay(CommitDelay, quiet.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Which of the two it was is decided under the lock below, not here?
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_quiet, quiet))
            {
                return;
            }
        }

        Commit();
    }

    private void Commit()
    {
        AppSettings? draft;
        int edits;

        lock (_gate)
        {
            draft = _pending;
            edits = _pendingEdits;

            // Pulled here rather than after the save, so a draft is claimed exactly once, however
            // many callers arrive: a superseded quiet window and a flush both reach this.
            _pending = null;
            _pendingEdits = 0;
        }

        if (draft is null)
        {
            return;
        }

        // A count and nothing else. This type holds the object carrying the user's API keys and
        // preset prompts, so no field name and no value is written down.
        _logger.LogDebug("Committing {ChangedSettings} settings changes.", edits);

        try
        {
            _store.Save(draft);
        }
        catch (SettingsException exception)
        {
            _logger.LogError(exception, "{ChangedSettings} settings changes could not be saved.", edits);
            _notifications.Notify(SaveFailureTitle, exception.Message);
        }
    }
}
