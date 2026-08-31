namespace Pisum.Whisper.Core.Transcription;

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Model listing and key testing for the settings window, mirroring the reference's
/// <c>GeminiProvider::list_models</c> and <c>test_connection</c> (<c>ai/gemini.rs:232-338</c>).
/// </summary>
/// <remarks>
/// Neither call retries. Both are initiated by a user looking at a window they can click again,
/// unlike a dictation the user has already spoken and cannot repeat for free.
/// </remarks>
public sealed class GeminiKeyProbe(IHttpClientFactory httpClientFactory, ILogger<GeminiKeyProbe> logger)
    : IGeminiKeyProbe
{
    private const string ModelPrefix = "models/";

    public async Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey,
                                                                  CancellationToken cancellationToken)
    {
        var (status, body) = await SendAsync(
            HttpMethod.Get, "models", apiKey, null, cancellationToken).ConfigureAwait(false);

        if ((int) status is < 200 or > 299)
        {
            throw Scrub(GeminiProvider.FailureFor(status, body), apiKey);
        }

        GeminiModelsResponse? response;
        try
        {
            response = JsonSerializer.Deserialize(body, GeminiJsonContext.Default.GeminiModelsResponse);
        }
        catch (JsonException exception)
        {
            throw new TranscriptionException(
                "The model list could not be read.", ErrorCategory.Transcription, exception);
        }

        var models = (response?.Models ?? [])
            .Where(entry => entry.Name is not null
                            && entry.SupportedGenerationMethods?.Contains("generateContent") == true)
            .Select(entry =>
            {
                var id = entry.Name![ModelPrefix.Length..];
                return new GeminiModel(id, string.IsNullOrWhiteSpace(entry.DisplayName) ? id : entry.DisplayName);
            })
            .ToList();

        logger.LogInformation("Gemini offered {Count} models that support content generation.", models.Count);
        return models;
    }

    public async Task<KeyProbeResult> TestConnectionAsync(string apiKey,
                                                          string? model,
                                                          CancellationToken cancellationToken)
    {
        var request = new GeminiRequest
        {
            // Deliberately no system instruction: this checks the key and model, not a preset.
            Contents = [new GeminiContent {Parts = [new GeminiPart {Text = "Respond with only: OK"}]}],
            GenerationConfig = new GeminiGenerationConfig {Temperature = 0.1f, MaxOutputTokens = 10},
        };

        var effectiveModel = string.IsNullOrWhiteSpace(model) ? GeminiProvider.DefaultModel : model;
        var payload = JsonSerializer.Serialize(request, GeminiJsonContext.Default.GeminiRequest);

        try
        {
            var (status, body) = await SendAsync(
                HttpMethod.Post,
                $"{ModelPrefix}{effectiveModel}:generateContent",
                apiKey,
                payload,
                cancellationToken).ConfigureAwait(false);

            if ((int) status is < 200 or > 299)
            {
                var failure = Scrub(GeminiProvider.FailureFor(status, body), apiKey);
                logger.LogInformation("Gemini connection test failed with {Category}.", failure.Category);
                return new KeyProbeResult(false, failure.Message, failure.Category);
            }

            // Raises if the response carries no usable text, which a working key and model always do.
            GeminiProvider.ExtractText(body);

            logger.LogInformation("Gemini connection test succeeded for model {Model}.", effectiveModel);
            return new KeyProbeResult(true, "Connection succeeded.", null);
        }
        catch (TranscriptionException failure)
        {
            return new KeyProbeResult(false, failure.Message, failure.Category);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Gemini connection test could not reach the service.");
            return new KeyProbeResult(false, "Gemini could not be reached.", ErrorCategory.Network);
        }
    }

    /// <summary>
    /// Removes the key from text that came back from Gemini before it is shown or thrown. Google's
    /// error bodies do not echo the key today, but this text is displayed verbatim and the settings
    /// file it came from holds credentials.
    /// </summary>
    internal static TranscriptionException Scrub(TranscriptionException failure, string apiKey)
    {
        return string.IsNullOrEmpty(apiKey) || !failure.Message.Contains(apiKey, StringComparison.Ordinal)
            ? failure
            : new TranscriptionException(
                failure.Message.Replace(apiKey, "[key]", StringComparison.Ordinal), failure.Category);
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpMethod method,
                                                                       string relativeUri,
                                                                       string apiKey,
                                                                       string? payload,
                                                                       CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(GeminiHttpClient.Name);

        using var message = new HttpRequestMessage(method, relativeUri);
        message.Headers.Add(GeminiHttpClient.ApiKeyHeader, apiKey);

        if (payload is not null)
        {
            message.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return (response.StatusCode, body);
    }
}
