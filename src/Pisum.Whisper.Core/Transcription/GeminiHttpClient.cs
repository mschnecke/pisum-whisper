namespace Pisum.Whisper.Core.Transcription;

/// <summary>
/// The shared configuration of the named <see cref="HttpClient"/> every Gemini call goes through.
/// </summary>
internal static class GeminiHttpClient
{
    public const string Name = "gemini";

    /// <summary>
    /// Where the API key travels. Deliberately not the reference's <c>?key=</c> query parameter
    /// (<c>ai/gemini.rs:31-36</c>): <c>IHttpClientFactory</c> logs every request URI at
    /// <c>Information</c> and the default <c>logLevel</c> is <c>info</c>, so the query form would
    /// write the user's API key into the log file that change 10 puts one click away.
    /// </summary>
    public const string ApiKeyHeader = "x-goog-api-key";

    public static readonly Uri BaseAddress = new("https://generativelanguage.googleapis.com/v1beta/");

    /// <summary>
    /// Per request, not per transcription. The reference sets no timeout at all (reqwest's default),
    /// so a hung upload hangs the dictation for ever. A budget spanning retries and providers is the
    /// dictation pipeline's to impose, through the cancellation token it already passes.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
}
