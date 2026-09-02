namespace Pisum.Whisper.App.Tests.Settings;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Tests;
using Pisum.Whisper.Core.Notifications;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// A real <see cref="SettingsStore"/> over a temporary file and a <see cref="SettingsEditor"/> whose
/// quiet window is opened and closed by the test rather than by the clock, so nothing here waits
/// 400 ms of real time.
/// </summary>
public abstract class SettingsEditorTestBase : IDisposable
{
    private readonly string _directory;

    private readonly List<TaskCompletionSource> _delays = [];

    private readonly List<SettingsEditor> _editors = [];

    private readonly Lock _gate = new();

    protected SettingsEditorTestBase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);

        Store = new SettingsStore(
            NullLogger<SettingsStore>.Instance, Path.Combine(_directory, ".pisum-whisper.json"));
        Store.Load();
        Store.Changed += (_, _) => Saves++;
    }

    /// <summary>
    /// Drains every editor this test made before the temporary home goes.
    /// </summary>
    /// <remarks>
    /// A commit runs on a pooled thread, and <c>SettingsWindow</c>'s <c>Closing</c> handler starts one
    /// without awaiting it. A commit that outlived this directory would write into a delete-pending
    /// directory, which Windows answers with an <see cref="UnauthorizedAccessException"/> in whatever
    /// test happened to be running — a failure with no relationship to what it was measuring.
    /// </remarks>
    public virtual void Dispose()
    {
        foreach (var editor in _editors)
        {
            editor.FlushAsync().GetAwaiter().GetResult();
        }

        Directory.Delete(_directory, true);
    }

    protected SettingsStore Store { get; }

    /// <summary>How many times the store has published a save, counted from its own event.</summary>
    protected int Saves { get; private set; }

    protected SettingsEditor NewEditor(ILogger<SettingsEditor>? logger = null,
                                       INotificationService? notifications = null)
    {
        var editor = new SettingsEditor(
            Store,
            logger ?? NullLogger<SettingsEditor>.Instance,
            notifications ?? new RecordingNotificationService(),
            DelayAsync);
        _editors.Add(editor);
        return editor;
    }

    /// <summary>Ends the quiet window the editor is currently waiting out.</summary>
    protected void CompleteQuietWindow()
    {
        TaskCompletionSource[] pending;

        lock (_gate)
        {
            pending = [.. _delays];
            _delays.Clear();
        }

        foreach (var delay in pending)
        {
            delay.TrySetResult();
        }
    }

    /// <summary>
    /// Waits for the commit an ended quiet window scheduled. The commit runs as a continuation on a
    /// pooled thread, so an assertion made the instant the delay completes is a race rather than a
    /// test; this bounds the wait without making the outcome depend on the machine.
    /// </summary>
    protected static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource();

        lock (_gate)
        {
            _delays.Add(completion);
        }

        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }
}
