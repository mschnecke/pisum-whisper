namespace Pisum.Whisper.Core.Transcription;

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Audio;

/// <summary>
/// One API key and model against Gemini's <c>generateContent</c>, with the reference's retry policy
/// (<c>ai/gemini.rs</c>) reproduced and one part of it corrected — see <see cref="IsRetryable"/>.
/// </summary>
internal sealed class GeminiProvider : ITranscriptionProvider
{
    internal const string DefaultModel = "gemini-2.5-flash-lite";

    /// <summary>
    /// Three attempts, two waits. The reference's <c>MAX_RETRIES = 3</c> bounds its loop, so it makes
    /// three attempts separated by <c>RETRY_DELAY_MS * (attempt + 1)</c> — 1 s then 2 s.
    /// </summary>
    internal const int MaxAttempts = 3;

    /// <summary>
    /// Gemini caps a request carrying <c>inlineData</c> at "20 MB total (including prompts and all
    /// files)", above which the Files API is required. Base64 inflates by 4/3, so 14 MiB of encoded
    /// audio becomes about 19.6 MB on the wire — under the cap even read as a decimal 20,000,000,
    /// with roughly 400 KB left for the system prompt and JSON envelope. 15 MiB would not be: it
    /// base64-encodes to exactly 20,971,520 bytes, over the cap before the envelope is counted.
    /// Opus at 24 kbps reaches this after about 81 minutes; WAV at 48 kHz mono 16-bit reaches it
    /// after about 2 min 33 s, well inside the 600 s default recording maximum.
    /// </summary>
    internal const int MaxInlineAudioBytes = 14 * 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;

    private readonly string _apiKey;

    private readonly string _model;

    private readonly ILogger _logger;

    private readonly Func<int, CancellationToken, Task> _backoff;

    public GeminiProvider(IHttpClientFactory httpClientFactory, string apiKey, string? model, ILogger logger)
        : this(httpClientFactory, apiKey, model, logger, DefaultBackoffAsync)
    {
    }

    /// <summary>Takes the backoff as a delegate so retry tests do not wait three real seconds,
    /// following <see cref="AudioEncoder"/>'s injected-writer precedent.</summary>
    internal GeminiProvider(IHttpClientFactory httpClientFactory,
                            string apiKey,
                            string? model,
                            ILogger logger,
                            Func<int, CancellationToken, Task> backoff)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _logger = logger;
        _backoff = backoff;
    }

    public async Task<string> TranscribeAsync(EncodedAudio audio,
                                              string systemPrompt,
                                              CancellationToken cancellationToken)
    {
        // Checked here rather than in the pool so an oversized recording fails once instead of once
        // per configured key — and it is provider-specific knowledge, which is where it belongs.
        if (audio.Bytes.Length > MaxInlineAudioBytes)
        {
            throw new TranscriptionException(
                $"The recording is too large to send: {audio.Bytes.Length / (1024 * 1024)} MB as " +
                $"{audio.ActualFormat}, and the limit is {MaxInlineAudioBytes / (1024 * 1024)} MB. " +
                "Record for less time, or switch the audio format to Opus.",
                ErrorCategory.Configuration);
        }

        var request = new GeminiRequest
        {
            SystemInstruction = new GeminiSystemInstruction
            {
                Parts = [new GeminiPart {Text = systemPrompt}],
            },
            Contents =
            [
                new GeminiContent
                {
                    Parts =
                    [
                        new GeminiPart
                        {
                            InlineData = new GeminiInlineData
                            {
                                MimeType = audio.MimeType,
                                Data = Convert.ToBase64String(audio.Bytes),
                            },
                        },
                    ],
                },
            ],
            GenerationConfig = new GeminiGenerationConfig {Temperature = 0.1f, MaxOutputTokens = 8192},
        };

        var text = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

        // The character count, not the characters: transcripts are the user's speech.
        _logger.LogInformation("Transcription complete: {Characters} characters.", text.Length);
        return text;
    }

    /// <summary>
    /// Whether a response is worth another attempt. Unlike the reference, which tests this predicate
    /// <em>before</em> the status (<c>ai/gemini.rs:102</c>), a successful response is never retryable:
    /// on a 200 the body is the transcript, so a user dictating "we hit the rate limit yesterday"
    /// would otherwise have their dictation retried three times and then fail.
    /// </summary>
    internal static bool IsRetryable(HttpStatusCode status, string body)
    {
        if (IsSuccess(status))
        {
            return false;
        }

        if (status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            return true;
        }

        return body.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
               || body.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
               || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the transcript out of a successful response, mirroring the reference's
    /// <c>extract_text</c> including its two distinct empty-response messages.
    /// </summary>
    internal static string ExtractText(string body)
    {
        GeminiResponse? response;
        try
        {
            response = JsonSerializer.Deserialize(body, GeminiJsonContext.Default.GeminiResponse);
        }
        catch (JsonException exception)
        {
            throw new TranscriptionException(
                "Gemini's response could not be read.", ErrorCategory.Transcription, exception);
        }

        if (response?.Error?.Message is {Length: > 0} message)
        {
            throw new TranscriptionException(message, ErrorCategory.Transcription);
        }

        var text = response?.Candidates is {Count: > 0} candidates
            ? candidates[0].Content?.Parts is {Count: > 0} parts ? parts[0].Text : null
            : null;

        if (text is null)
        {
            throw new TranscriptionException("Gemini generated no response.", ErrorCategory.Transcription);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new TranscriptionException("Gemini returned an empty response.", ErrorCategory.Transcription);
        }

        return text;
    }

    /// <summary>
    /// Builds the failure for an <em>unsuccessful</em> response. Only ever called for one, which is
    /// what makes including part of the body safe: it is Google's error JSON, never a transcript.
    /// </summary>
    internal static TranscriptionException FailureFor(HttpStatusCode status, string body)
    {
        var category = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ErrorCategory.Authentication,
            HttpStatusCode.TooManyRequests => ErrorCategory.RateLimit,
            _ when body.Contains("quota", StringComparison.OrdinalIgnoreCase) => ErrorCategory.RateLimit,
            _ => ErrorCategory.Transcription,
        };

        var detail = body.Length > 200 ? body[..200] : body;
        return new TranscriptionException($"Gemini returned {(int) status}: {detail}", category);
    }

    private static bool IsSuccess(HttpStatusCode status)
    {
        return (int) status is >= 200 and <= 299;
    }

    private static Task DefaultBackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        return Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
    }

    private async Task<string> SendWithRetryAsync(GeminiRequest request, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(GeminiHttpClient.Name);
        var payload = JsonSerializer.Serialize(request, GeminiJsonContext.Default.GeminiRequest);
        var relativeUri = $"models/{_model}:generateContent";

        TranscriptionException? lastFailure = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            HttpStatusCode status;
            string body;

            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, relativeUri)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };

                // Never logged: the request headers carry the key, which is why nothing here writes
                // the message or its headers to the log.
                message.Headers.Add(GeminiHttpClient.ApiKeyHeader, _apiKey);

                using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                status = response.StatusCode;
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException
                && !cancellationToken.IsCancellationRequested)
            {
                // A transport failure or this client's own timeout. The reference retries these too,
                // and a connection reset is exactly what a retry is for.
                lastFailure = new TranscriptionException(
                    "Gemini could not be reached.", ErrorCategory.Network, exception);

                if (attempt < MaxAttempts)
                {
                    _logger.LogWarning(
                        "Gemini attempt {Attempt} of {MaxAttempts} did not reach the service; retrying.",
                        attempt,
                        MaxAttempts);

                    await _backoff(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                break;
            }

            if (IsRetryable(status, body))
            {
                lastFailure = FailureFor(status, body);

                if (attempt < MaxAttempts)
                {
                    _logger.LogWarning(
                        "Gemini attempt {Attempt} of {MaxAttempts} returned {Status}; retrying.",
                        attempt,
                        MaxAttempts,
                        (int) status);

                    await _backoff(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                break;
            }

            if (!IsSuccess(status))
            {
                // Everything not worth retrying fails now rather than burning seconds on an error
                // that will not resolve.
                throw FailureFor(status, body);
            }

            return ExtractText(body);
        }

        throw lastFailure ?? new TranscriptionException(
            "Gemini did not answer after retrying.", ErrorCategory.Network);
    }
}
