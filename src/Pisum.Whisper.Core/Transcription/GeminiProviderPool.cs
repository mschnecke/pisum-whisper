namespace Pisum.Whisper.Core.Transcription;

using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// Spreads transcriptions across the enabled provider entries and falls back through all of them on
/// failure, mirroring the reference's <c>ProviderPool</c> (<c>ai/pool.rs</c>).
/// </summary>
/// <remarks>
/// It implements <see cref="ITranscriptionProvider"/> itself, so the dictation pipeline depends on
/// one contract and never learns that pools exist. It is also never rebuilt: the reference copies
/// settings into a global pool in <c>apply_settings</c> because it has no authoritative in-memory
/// store, whereas <see cref="SettingsStore.Current"/> is exactly that. Reading the entries per call
/// makes a rebuild step, a change subscription and a lock all unnecessary — the only durable state
/// here is the round-robin cursor.
/// </remarks>
public sealed class GeminiProviderPool : ITranscriptionProvider
{
    internal const string NoProvidersMessage =
        "No AI providers configured. Please add a provider in Settings.";

    private readonly SettingsStore _settings;

    private readonly ILogger<GeminiProviderPool> _logger;

    private readonly Func<ProviderConfig, ITranscriptionProvider> _createProvider;

    private int _cursor;

    public GeminiProviderPool(SettingsStore settings,
                              IHttpClientFactory httpClientFactory,
                              ILoggerFactory loggerFactory)
        : this(
            settings,
            loggerFactory.CreateLogger<GeminiProviderPool>(),
            entry => new GeminiProvider(
                httpClientFactory, entry.ApiKey, entry.Model, loggerFactory.CreateLogger<GeminiProvider>()))
    {
    }

    /// <summary>Takes the per-entry construction as a delegate so the round-robin and fallback tests
    /// need no HTTP handler at all, following <see cref="AudioEncoder"/>'s precedent.</summary>
    internal GeminiProviderPool(SettingsStore settings,
                                ILogger<GeminiProviderPool> logger,
                                Func<ProviderConfig, ITranscriptionProvider> createProvider,
                                int initialCursor = -1)
    {
        _settings = settings;
        _logger = logger;
        _createProvider = createProvider;
        _cursor = initialCursor;
    }

    public async Task<string> TranscribeAsync(EncodedAudio audio,
                                              string systemPrompt,
                                              CancellationToken cancellationToken)
    {
        // Snapshotted once, so a settings save mid-transcription cannot change the entry set between
        // fallback attempts.
        var entries = _settings.Current.Providers.Where(entry => entry.Enabled).ToList();

        if (entries.Count == 0)
        {
            throw new TranscriptionException(NoProvidersMessage, ErrorCategory.Configuration);
        }

        // Cast through uint before the modulo: the cursor wraps to int.MinValue after about 2.1
        // billion dictations, and a negative index would throw rather than wrap.
        var start = (int) ((uint) Interlocked.Increment(ref _cursor) % (uint) entries.Count);

        var failures = new List<string>();
        var categories = new List<ErrorCategory>();

        for (var offset = 0; offset < entries.Count; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[(start + offset) % entries.Count];

            try
            {
                return await _createProvider(entry)
                    .TranscribeAsync(audio, systemPrompt, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TranscriptionException failure)
            {
                // The entry's id and category, never its key and never the message's payload.
                _logger.LogWarning(
                    "Provider {ProviderId} failed with {Category}; {Remaining} left to try.",
                    entry.Id,
                    failure.Category,
                    entries.Count - offset - 1);

                failures.Add($"{entry.Id}: {failure.Message}");
                categories.Add(failure.Category);
            }
        }

        _logger.LogError("All {Count} configured providers failed.", entries.Count);

        // One shared category survives aggregation: a lone misconfigured key must still surface as
        // an authentication failure rather than being flattened into a generic one.
        var category = categories.Distinct().Count() == 1 ? categories[0] : ErrorCategory.Transcription;

        throw new TranscriptionException($"All providers failed: {string.Join("; ", failures)}", category);
    }
}
