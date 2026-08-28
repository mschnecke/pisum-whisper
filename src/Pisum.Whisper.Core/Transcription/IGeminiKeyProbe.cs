namespace Pisum.Whisper.Core.Transcription;

/// <summary>The outcome of testing a key, shaped to be displayed rather than thrown.</summary>
/// <remarks>
/// The reference returns <c>Result&lt;bool, AppError&gt;</c> from its connection test but never
/// returns <c>Ok(false)</c> — it is <c>Ok(true)</c> or an error, so the boolean carries nothing the
/// settings window can show. This carries both the outcome and the text to show, so a failed test
/// renders without wrapping a UI command in a <c>try</c>.
/// </remarks>
public sealed record KeyProbeResult(bool Succeeded, string Message, ErrorCategory? Category);

/// <summary>
/// Asks Gemini about an API key — including one the user has just typed and not yet saved, which is
/// why these are not on <see cref="ITranscriptionProvider"/>: they take a key rather than use a
/// configured entry, and the dictation pipeline never calls them.
/// </summary>
public interface IGeminiKeyProbe
{
    /// <summary>Lists the models <paramref name="apiKey"/> may use for content generation.</summary>
    /// <exception cref="TranscriptionException">The listing could not be retrieved.</exception>
    Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey, CancellationToken cancellationToken);

    /// <summary>Checks that <paramref name="apiKey"/> can actually generate content.</summary>
    Task<KeyProbeResult> TestConnectionAsync(string apiKey, string? model, CancellationToken cancellationToken);
}
