namespace Pisum.Whisper.App.Settings.ViewModels;

using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Transcription;

/// <summary>
/// The model listings fetched during one lifetime of the settings window, keyed by an API key.
/// </summary>
/// <remarks>
/// <para>
/// The reference caches per <c>providerType:apiKey</c>; there is one provider type here, so the key
/// alone is the cache key. It is a listing per distinct key rather than per entry, so two entries
/// sharing a key cost one request, and Refresh is the only thing that fetches a key twice.
/// </para>
/// <para>
/// A failed listing is not cached and is not thrown to the caller: the dropdown keeps its default
/// option and stays usable, which is a better answer than a command that raises out of a UI event.
/// The key itself is never logged.
/// </para>
/// </remarks>
public sealed class ModelListCache(ILogger<ModelListCache> logger)
{
    private readonly Dictionary<string, IReadOnlyList<GeminiModel>> _byApiKey = new(StringComparer.Ordinal);

    /// <summary>The models <paramref name="apiKey"/> may use, fetched once per distinct key.</summary>
    public async Task<IReadOnlyList<GeminiModel>> GetAsync(string apiKey,
                                                           IGeminiKeyProbe probe,
                                                           CancellationToken cancellationToken)
    {
        if (_byApiKey.TryGetValue(apiKey, out var cached))
        {
            return cached;
        }

        try
        {
            var models = await probe.ListModelsAsync(apiKey, cancellationToken).ConfigureAwait(true);
            _byApiKey[apiKey] = models;
            return models;
        }
        catch (TranscriptionException exception)
        {
            // Logged by category, never by key or message body, and deliberately not cached: the
            // next Refresh should try again rather than serve a failure.
            logger.LogInformation("Listing the models for a key failed with {Category}.", exception.Category);
            return [];
        }
    }

    /// <summary>Drops <paramref name="apiKey"/>'s listing, so the next request fetches it again.</summary>
    public void Forget(string apiKey)
    {
        _byApiKey.Remove(apiKey);
    }
}
