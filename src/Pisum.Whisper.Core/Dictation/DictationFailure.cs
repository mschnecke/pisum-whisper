namespace Pisum.Whisper.Core.Dictation;

using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Transcription;

/// <summary>
/// Turns a failed dictation into the title and message the user is shown.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a function rather than a seam. Change 11 adds the notification transport and
/// the forced-versus-suppressible policy — that policy is a read of <c>ShowTrayNotifications</c> and
/// is notification business, not the pipeline's — and it calls this to decide what to say. An event
/// or an <c>IUserNotifier</c> stub would compute these strings and drop them for three changes; a
/// function has a real consumer today, the log, and is directly testable.
/// </para>
/// <para>
/// <b>The title is chosen from what failed, never by matching message text.</b> The reference
/// substring-matches its error messages (<c>hotkey/manager.rs:488-515</c>), which is precisely what
/// <see cref="ErrorCategory"/> exists to avoid: three of the four kinds below are distinguished by
/// type, and the fourth carries its category from where it was raised.
/// </para>
/// </remarks>
internal static class DictationFailure
{
    internal const string RecordingErrorTitle = "Recording Error";

    internal const string OutputErrorTitle = "Output Error";

    internal const string TranscriptionErrorTitle = "Transcription Error";

    internal const string UnexpectedErrorTitle = "Unexpected Error";

    internal const string UnexpectedErrorMessage =
        "An unexpected error occurred. Check the log for details.";

    internal const string BudgetExpiredMessage =
        "The transcription took too long and was abandoned.";

    /// <summary>Describes <paramref name="exception"/> as a title and a message written to be shown as-is.</summary>
    public static (string Title, string Message) Describe(Exception exception)
    {
        return exception switch
        {
            // Capture and encoding. Deliberately not split further: the reference adds a macOS-only
            // "Microphone Access Required" branch by substring-matching "No input device", which is both
            // the rejected mechanism and a guess — spike S2 passed on the M4 with the microphone
            // accessible, so nobody has observed what a refused grant actually looks like.
            AudioException => (RecordingErrorTitle, exception.Message),

            TranscriptionException failure => (TitleFor(failure.Category), failure.Message),

            TextOutputException => (OutputErrorTitle, exception.Message),

            // The transcription budget expired. Shutdown produces the same exception and is filtered out
            // before this is reached because the user asked for that one.
            OperationCanceledException => (TranscriptionErrorTitle, BudgetExpiredMessage),

            _ => (UnexpectedErrorTitle, UnexpectedErrorMessage),
        };
    }

    private static string TitleFor(ErrorCategory category)
    {
        return category switch
        {
            ErrorCategory.Configuration => "Configuration Error",
            ErrorCategory.Network => "Network Error",
            ErrorCategory.Authentication => "Authentication Error",
            ErrorCategory.RateLimit => "Rate Limit Error",
            _ => TranscriptionErrorTitle,
        };
    }
}
