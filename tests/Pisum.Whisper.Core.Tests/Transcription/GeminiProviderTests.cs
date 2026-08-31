namespace Pisum.Whisper.Core.Tests.Transcription;

using System.Net;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Tasks 4.2-4.8 — the single-key client, exercised against a scripted handler. Nothing here reaches
/// the network or needs an API key.
/// </summary>
public sealed class GeminiProviderTests
{
    private const string ApiKey = "AIza-not-a-real-key";
    private const string TranscriptBody = """
        { "candidates": [ { "content": { "parts": [ { "text": "hello world" } ] } } ] }
        """;

    private static readonly EncodedAudio Audio = new([1, 2, 3], EncodedAudio.OpusMimeType, AudioFormat.Opus);

    // ---- Task 4.2: the key travels in a header, never the URI ----

    [Fact]
    public async Task TheRequest_CarriesTheKeyInAHeaderAndNothingInTheQuery()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, TranscriptBody);

        await Provider(handler).TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);

        var request = handler.Requests.Single();
        request.ApiKey.ShouldBe(ApiKey);
        request.RequestUri!.Query.ShouldBeEmpty();
        request.RequestUri.AbsoluteUri.ShouldNotContain(ApiKey);
        request.RequestUri.AbsoluteUri.ShouldEndWith($"models/{GeminiProvider.DefaultModel}:generateContent");
    }

    [Fact]
    public async Task TheRequest_CarriesTheAudioAndThePromptGeminiExpects()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, TranscriptBody);

        await Provider(handler).TranscribeAsync(Audio, "Transcribe carefully.", CancellationToken.None);

        var body = handler.Requests.Single().Body;
        body.ShouldContain("\"text\":\"Transcribe carefully.\"");
        body.ShouldContain($"\"mimeType\":\"{EncodedAudio.OpusMimeType}\"");
        body.ShouldContain($"\"data\":\"{Convert.ToBase64String(Audio.Bytes)}\"");
        body.ShouldContain("\"maxOutputTokens\":8192");
    }

    [Fact]
    public async Task AFallbackToWav_IsTaggedWithTheWavMimeType()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, TranscriptBody);
        var wav = new EncodedAudio([9, 9], EncodedAudio.WavMimeType, AudioFormat.Wav);

        await Provider(handler).TranscribeAsync(wav, "Transcribe.", CancellationToken.None);

        handler.Requests.Single().Body.ShouldContain($"\"mimeType\":\"{EncodedAudio.WavMimeType}\"");
    }

    // ---- Task 4.3: the inline-size guard ----

    [Fact]
    public async Task AudioOverTheInlineCeiling_IsRejectedBeforeAnyRequest()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, TranscriptBody);
        var oversized = new EncodedAudio(
            new byte[GeminiProvider.MaxInlineAudioBytes + 1], EncodedAudio.WavMimeType, AudioFormat.Wav);

        var failure = await Should.ThrowAsync<TranscriptionException>(
            () => Provider(handler).TranscribeAsync(oversized, "Transcribe.", CancellationToken.None));

        failure.Category.ShouldBe(ErrorCategory.Configuration);
        failure.Message.ShouldContain("Wav");
        failure.Message.ShouldContain("14 MB");
        handler.SendCount.ShouldBe(0);
    }

    [Fact]
    public async Task AudioAtTheInlineCeiling_IsSent()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, TranscriptBody);
        var atLimit = new EncodedAudio(
            new byte[GeminiProvider.MaxInlineAudioBytes], EncodedAudio.WavMimeType, AudioFormat.Wav);

        await Provider(handler).TranscribeAsync(atLimit, "Transcribe.", CancellationToken.None);

        handler.SendCount.ShouldBe(1);
    }

    // ---- Task 4.4: reading the transcript out ----

    [Fact]
    public void ExtractText_ReturnsTheCandidateText() =>
        GeminiProvider.ExtractText(TranscriptBody).ShouldBe("hello world");

    [Theory]
    [InlineData("""{ "candidates": [] }""", TestDisplayName = "no candidate")]
    [InlineData("""{ "candidates": [ { "content": { "parts": [] } } ] }""", TestDisplayName = "no part")]
    [InlineData("""{ "candidates": [ { "content": { "parts": [ { } ] } } ] }""", TestDisplayName = "no text")]
    [InlineData("""{ }""", TestDisplayName = "nothing at all")]
    public void ExtractText_WithNothingUsable_RaisesTranscription(string body)
    {
        Should.Throw<TranscriptionException>(() => GeminiProvider.ExtractText(body))
            .Category.ShouldBe(ErrorCategory.Transcription);
    }

    [Fact]
    public void ExtractText_WithWhitespaceOnly_RaisesTranscription()
    {
        const string body = """{ "candidates": [ { "content": { "parts": [ { "text": "   " } ] } } ] }""";

        Should.Throw<TranscriptionException>(() => GeminiProvider.ExtractText(body))
            .Category.ShouldBe(ErrorCategory.Transcription);
    }

    [Fact]
    public void ExtractText_WithABodyLevelError_SurfacesItsMessage()
    {
        const string body = """{ "error": { "code": 400, "message": "API key not valid" } }""";

        var failure = Should.Throw<TranscriptionException>(() => GeminiProvider.ExtractText(body));

        failure.Message.ShouldBe("API key not valid");
        failure.Category.ShouldBe(ErrorCategory.Transcription);
    }

    // ---- Task 4.5: the corrected retry predicate ----

    [Fact]
    public void ATranscriptMentioningRateLimits_IsNotRetryable()
    {
        // The reference tests this predicate before the status, so this exact transcript is retried
        // three times and then fails. For a dictation application these are ordinary words.
        GeminiProvider.IsRetryable(HttpStatusCode.OK, "we hit the rate limit yesterday").ShouldBeFalse();
        GeminiProvider.IsRetryable(HttpStatusCode.OK, "the server was overloaded").ShouldBeFalse();
    }

    [Fact]
    public void TransientStatusesAreRetryable()
    {
        GeminiProvider.IsRetryable(HttpStatusCode.ServiceUnavailable, "{}").ShouldBeTrue();
        GeminiProvider.IsRetryable(HttpStatusCode.TooManyRequests, "quota exceeded").ShouldBeTrue();
    }

    [Fact]
    public void AnUnsuccessfulBodyMentioningOverload_IsRetryable()
    {
        GeminiProvider.IsRetryable(HttpStatusCode.InternalServerError, "model is overloaded").ShouldBeTrue();
        GeminiProvider.IsRetryable(HttpStatusCode.BadRequest, "invalid argument").ShouldBeFalse();
    }

    // ---- Task 4.6: the retry loop ----

    [Fact]
    public async Task A503ThenSuccess_ReturnsTheTranscriptWithoutAThirdAttempt()
    {
        var handler = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.OK, TranscriptBody);

        var text = await Provider(handler).TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);

        text.ShouldBe("hello world");
        handler.SendCount.ShouldBe(2);
    }

    [Fact]
    public async Task ThreeRateLimits_ExhaustTheAttemptsAndRaiseRateLimit()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.TooManyRequests, "slow down");

        var failure = await Should.ThrowAsync<TranscriptionException>(
            () => Provider(handler).TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        failure.Category.ShouldBe(ErrorCategory.RateLimit);
        handler.SendCount.ShouldBe(GeminiProvider.MaxAttempts);
    }

    [Fact]
    public async Task APermanentError_FailsOnTheFirstAttempt()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.BadRequest, "invalid argument");

        await Should.ThrowAsync<TranscriptionException>(
            () => Provider(handler).TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        handler.SendCount.ShouldBe(1);
    }

    [Fact]
    public async Task ATransportFailure_IsRetriedAndReportedAsNetwork()
    {
        var handler = new StubHttpMessageHandler().Throws(new HttpRequestException("connection reset"));

        var failure = await Should.ThrowAsync<TranscriptionException>(
            () => Provider(handler).TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        failure.Category.ShouldBe(ErrorCategory.Network);
        handler.SendCount.ShouldBe(GeminiProvider.MaxAttempts);
    }

    [Fact]
    public async Task CancellationDuringBackoff_StopsWithoutAFurtherAttempt()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.ServiceUnavailable);
        using var cancellation = new CancellationTokenSource();

        // Cancels while the provider is waiting to retry, which is the window the plain
        // token-on-send check would miss.
        var provider = new GeminiProvider(
            new StubHttpClientFactory(handler),
            ApiKey,
            model: null,
            new RecordingLogger(),
            (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            });

        await Should.ThrowAsync<OperationCanceledException>(
            () => provider.TranscribeAsync(Audio, "Transcribe.", cancellation.Token));

        handler.SendCount.ShouldBe(1);
    }

    // ---- Task 4.7: categories ----

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ErrorCategory.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, ErrorCategory.Authentication)]
    [InlineData(HttpStatusCode.BadRequest, ErrorCategory.Transcription)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorCategory.Transcription)]
    public async Task AFailureStatus_IsCategorisedAtTheThrowSite(HttpStatusCode status, ErrorCategory expected)
    {
        var handler = new StubHttpMessageHandler().Respond(status, "refused");

        var failure = await Should.ThrowAsync<TranscriptionException>(
            () => Provider(handler).TranscribeAsync(Audio, "Transcribe.", CancellationToken.None));

        failure.Category.ShouldBe(expected);
    }

    [Fact]
    public void AQuotaFailure_IsRateLimitWhateverTheStatus() =>
        GeminiProvider.FailureFor(HttpStatusCode.BadRequest, "quota exceeded for this project")
            .Category.ShouldBe(ErrorCategory.RateLimit);

    [Fact]
    public void ALongErrorBody_IsTruncated()
    {
        var failure = GeminiProvider.FailureFor(HttpStatusCode.BadRequest, new string('x', 500));

        failure.Message.Length.ShouldBeLessThan(300);
    }

    // ---- Task 4.8: end to end, and what may be written down ----

    [Fact]
    public async Task ASuccessfulTranscription_ReturnsTheText()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, TranscriptBody);

        var text = await Provider(handler).TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);

        text.ShouldBe("hello world");
    }

    [Fact]
    public async Task NeitherTheTranscriptNorTheKey_IsEverLogged()
    {
        var handler = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.OK, TranscriptBody);
        var logger = new RecordingLogger();

        var provider = new GeminiProvider(
            new StubHttpClientFactory(handler), ApiKey, model: null, logger, (_, _) => Task.CompletedTask);

        await provider.TranscribeAsync(Audio, "Transcribe.", CancellationToken.None);

        logger.Messages.ShouldNotBeEmpty();
        foreach (var message in logger.Messages)
        {
            message.ShouldNotContain("hello world");
            message.ShouldNotContain(ApiKey);
        }

        // The character count, not the characters.
        logger.Messages.ShouldContain(message => message.Contains("11 characters"));
    }

    /// <summary>A provider whose retry backoff returns immediately, so no test waits three seconds.</summary>
    private static GeminiProvider Provider(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), ApiKey, model: null, new RecordingLogger(), (_, _) => Task.CompletedTask);
}
