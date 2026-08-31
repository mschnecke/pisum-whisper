namespace Pisum.Whisper.Core.Tests.Transcription;

using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Tasks 5.1-5.4 — selection, fallback and aggregation. Every test here substitutes the per-entry
/// construction, so no HTTP handler is involved.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class GeminiProviderPoolTests : IDisposable
{
    private static readonly EncodedAudio Audio = new([1, 2, 3], EncodedAudio.OpusMimeType, AudioFormat.Opus);

    private readonly string _home = string.Empty;

    public GeminiProviderPoolTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        Directory.Delete(_home, true);
    }

    // ---- Task 5.1: nothing to transcribe with ----

    [Fact]
    public async Task WithNoProviders_RaisesConfiguration()
    {
        var pool = Pool(Store());

        var failure = await Should.ThrowAsync<TranscriptionException>(() =>
            pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        failure.Category.ShouldBe(ErrorCategory.Configuration);
        failure.Message.ShouldBe(GeminiProviderPool.NoProvidersMessage);
    }

    [Fact]
    public async Task WithOnlyDisabledProviders_RaisesConfiguration()
    {
        var pool = Pool(Store(Entry("a", false), Entry("b", false)));

        var failure = await Should.ThrowAsync<TranscriptionException>(() =>
            pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        failure.Category.ShouldBe(ErrorCategory.Configuration);
    }

    [Fact]
    public async Task ADisabledEntry_IsNeverSelected()
    {
        var tried = new List<string>();
        var pool = Pool(Store(Entry("off", false), Entry("on")), tried, _ => "text");

        await pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);
        await pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);

        tried.ShouldAllBe(id => id == "on");
    }

    // ---- Task 5.2: round-robin selection ----

    [Fact]
    public async Task ConsecutiveTranscriptions_StartFromDifferentEntries()
    {
        var tried = new List<string>();
        var pool = Pool(Store(Entry("a"), Entry("b"), Entry("c")), tried, _ => "text");

        await pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);
        await pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);
        await pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);

        tried.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public async Task AWrappingCursor_DoesNotProduceANegativeIndex()
    {
        var tried = new List<string>();
        var pool = Pool(
            Store(Entry("a"), Entry("b"), Entry("c")), tried, _ => "text", int.MaxValue - 1);

        // The second call wraps the cursor to int.MinValue; an unsigned modulo is what keeps this
        // from throwing.
        await Should.NotThrowAsync(() => pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));
        await Should.NotThrowAsync(() => pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        tried.Count.ShouldBe(2);
    }

    // ---- Task 5.3: fallback and aggregation ----

    [Fact]
    public async Task WhenTheFirstEntryFails_TheNextOneAnswers()
    {
        var tried = new List<string>();
        var pool = Pool(
            Store(Entry("a"), Entry("b")),
            tried,
            id => id == "a"
                ? throw new TranscriptionException("a is broken", ErrorCategory.Network)
                : "from b");

        var text = await pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);

        text.ShouldBe("from b");
        tried.ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task WhenEveryEntryFails_TheFailuresAreAggregated()
    {
        var pool = Pool(
            Store(Entry("a"), Entry("b")),
            null,
            id => throw new TranscriptionException($"{id} is broken", ErrorCategory.Network));

        var failure = await Should.ThrowAsync<TranscriptionException>(() =>
            pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        failure.Message.ShouldStartWith("All providers failed:");
        failure.Message.ShouldContain("a: a is broken");
        failure.Message.ShouldContain("b: b is broken");
    }

    [Fact]
    public async Task WhenTheOnlyEntryIsRejected_TheCategorySurvives()
    {
        // The reference flattens this into "All providers failed: …", whose first substring test in
        // categorize_error is contains("provider") — so every cloud failure there is titled
        // "Configuration Error". A lone mistyped key must say so instead.
        var pool = Pool(
            Store(Entry("a")),
            null,
            _ => throw new TranscriptionException("key rejected", ErrorCategory.Authentication));

        var failure = await Should.ThrowAsync<TranscriptionException>(() =>
            pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        failure.Category.ShouldBe(ErrorCategory.Authentication);
    }

    [Fact]
    public async Task WhenEveryEntryIsRateLimited_TheCategorySurvives()
    {
        var pool = Pool(
            Store(Entry("a"), Entry("b"), Entry("c")),
            null,
            id => throw new TranscriptionException($"{id} is throttled", ErrorCategory.RateLimit));

        var failure = await Should.ThrowAsync<TranscriptionException>(() =>
            pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        failure.Category.ShouldBe(ErrorCategory.RateLimit);
    }

    [Fact]
    public async Task WhenEntriesFailDifferently_TheAggregateIsGeneric()
    {
        var pool = Pool(
            Store(Entry("a"), Entry("b")),
            null,
            id => throw new TranscriptionException(
                $"{id} is broken",
                id == "a" ? ErrorCategory.Authentication : ErrorCategory.Network));

        var failure = await Should.ThrowAsync<TranscriptionException>(() =>
            pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        failure.Category.ShouldBe(ErrorCategory.Transcription);
    }

    [Fact]
    public async Task Cancellation_StopsTheWalkWithoutTryingTheRest()
    {
        var tried = new List<string>();
        using var cancellation = new CancellationTokenSource();

        var pool = Pool(
            Store(Entry("a"), Entry("b"), Entry("c")),
            tried,
            _ =>
            {
                cancellation.Cancel();
                throw new TranscriptionException("failed", ErrorCategory.Network);
            });

        await Should.ThrowAsync<OperationCanceledException>(() =>
            pool.TranscribeAsync(Audio, "Transcribe.", cancellation.Token));

        tried.Count.ShouldBe(1);
    }

    // ---- Settings are read per call, never rebuilt ----

    [Fact]
    public async Task AnEntryAddedAfterConstruction_IsUsedWithoutARebuild()
    {
        var store = Store(Entry("a"));
        var tried = new List<string>();
        var pool = Pool(store, tried, _ => "text");

        await pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);

        store.Current.Providers.Add(Entry("b"));
        await pool.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);

        tried.ShouldBe(["a", "b"]);
    }

    private static ProviderConfig Entry(string id, bool enabled = true)
    {
        return new ProviderConfig {Id = id, ApiKey = $"key-for-{id}", Enabled = enabled};
    }

    private SettingsStore Store(params ProviderConfig[] entries)
    {
        var store = new SettingsStore(
            NullLogger<SettingsStore>.Instance, Path.Combine(_home, ".pisum-whisper.json"));

        store.Load();
        store.Current.Providers.AddRange(entries);
        return store;
    }

    /// <summary>
    /// A pool whose per-entry provider is a fake driven by <paramref name="answer"/> — the entry id
    /// in, either the transcript or a thrown failure out — recording each id it was asked for.
    /// </summary>
    private static GeminiProviderPool Pool(SettingsStore store,
                                           List<string>? tried = null,
                                           Func<string, string>? answer = null,
                                           int initialCursor = -1)
    {
        return new GeminiProviderPool(
            store,
            NullLogger<GeminiProviderPool>.Instance,
            entry =>
            {
                var provider = A.Fake<ITranscriptionProvider>();
                A.CallTo(() => provider.TranscribeAsync(
                        A<EncodedAudio>._, A<string>._, A<CancellationToken>._))
                    .ReturnsLazily(() =>
                    {
                        tried?.Add(entry.Id);
                        return Task.FromResult(answer!(entry.Id));
                    });

                return provider;
            },
            initialCursor);
    }
}
