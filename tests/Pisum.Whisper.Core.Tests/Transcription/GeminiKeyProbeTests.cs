namespace Pisum.Whisper.Core.Tests.Transcription;

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Tasks 6.1-6.3 — what change 10's settings window asks about a key the user has just typed.
/// </summary>
[UnitTest]
public sealed class GeminiKeyProbeTests
{
    private const string ApiKey = "AIza-not-a-real-key";

    private const string ModelsBody = """
                                      {
                                        "models": [
                                          { "name": "models/gemini-2.5-flash-lite", "displayName": "Gemini 2.5 Flash-Lite",
                                            "supportedGenerationMethods": [ "generateContent", "countTokens" ] },
                                          { "name": "models/embedding-001", "displayName": "Embedding 001",
                                            "supportedGenerationMethods": [ "embedContent" ] },
                                          { "name": "models/gemini-2.5-pro", "supportedGenerationMethods": [ "generateContent" ] }
                                        ]
                                      }
                                      """;

    private const string OkBody = """
                                  { "candidates": [ { "content": { "parts": [ { "text": "OK" } ] } } ] }
                                  """;

    // ---- Task 6.2: listing models ----

    [Fact]
    public async Task ListModels_KeepsOnlyGenerateContentAndStripsThePrefix()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, ModelsBody);

        var models = await Probe(handler).ListModelsAsync(ApiKey, CancellationToken.None);

        models.Select(model => model.Id).ShouldBe(["gemini-2.5-flash-lite", "gemini-2.5-pro"]);
    }

    [Fact]
    public async Task ListModels_FallsBackToTheIdWhenThereIsNoDisplayName()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, ModelsBody);

        var models = await Probe(handler).ListModelsAsync(ApiKey, CancellationToken.None);

        models[0].DisplayName.ShouldBe("Gemini 2.5 Flash-Lite");
        models[1].DisplayName.ShouldBe("gemini-2.5-pro");
    }

    [Fact]
    public async Task ListModels_SendsTheKeyInAHeaderAndNothingInTheQuery()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, ModelsBody);

        await Probe(handler).ListModelsAsync(ApiKey, CancellationToken.None);

        var request = handler.Requests.Single();
        request.ApiKey.ShouldBe(ApiKey);
        request.RequestUri!.Query.ShouldBeEmpty();
        request.RequestUri.AbsoluteUri.ShouldNotContain(ApiKey);
    }

    [Fact]
    public async Task ListModels_WithARejectedKey_Raises()
    {
        var handler = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.Unauthorized, "API key not valid");

        var failure =
            await Should.ThrowAsync<TranscriptionException>(() =>
                Probe(handler).ListModelsAsync(ApiKey, CancellationToken.None));

        failure.Category.ShouldBe(ErrorCategory.Authentication);
        handler.SendCount.ShouldBe(1);
    }

    // ---- Task 6.3: testing a key ----

    [Fact]
    public async Task TestConnection_WithAValidKey_Succeeds()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, OkBody);

        var result = await Probe(handler).TestConnectionAsync(ApiKey, null, CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.Category.ShouldBeNull();
    }

    [Fact]
    public async Task TestConnection_SendsNoSystemInstruction()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, OkBody);

        await Probe(handler).TestConnectionAsync(ApiKey, null, CancellationToken.None);

        handler.Requests.Single().Body.ShouldNotContain("systemInstruction");
    }

    [Fact]
    public async Task TestConnection_UsesTheModelItWasGiven()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, OkBody);

        await Probe(handler).TestConnectionAsync(ApiKey, "gemini-2.5-pro", CancellationToken.None);

        handler.Requests.Single().RequestUri!.AbsoluteUri
            .ShouldEndWith("models/gemini-2.5-pro:generateContent");
    }

    [Fact]
    public async Task TestConnection_WithARejectedKey_ReportsFailureRatherThanThrowing()
    {
        var handler = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.Unauthorized, "API key not valid. Please pass a valid API key.");

        var result = await Probe(handler).TestConnectionAsync(ApiKey, null, CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Category.ShouldBe(ErrorCategory.Authentication);
        result.Message.ShouldContain("API key not valid");
        result.Message.ShouldNotContain(ApiKey);
    }

    [Fact]
    public async Task TestConnection_WhenGeminiCannotBeReached_ReportsNetwork()
    {
        var handler = new StubHttpMessageHandler().Throws(new HttpRequestException("connection reset"));

        var result = await Probe(handler).TestConnectionAsync(ApiKey, null, CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Category.ShouldBe(ErrorCategory.Network);

        // Unlike a dictation, a window can simply be clicked again.
        handler.SendCount.ShouldBe(1);
    }

    [Fact]
    public void AnEchoedKey_IsScrubbedFromWhatIsDisplayed()
    {
        var failure = new TranscriptionException(
            $"Gemini returned 400: key {ApiKey} is malformed", ErrorCategory.Transcription);

        GeminiKeyProbe.Scrub(failure, ApiKey).Message.ShouldBe("Gemini returned 400: key [key] is malformed");
    }

    private static GeminiKeyProbe Probe(StubHttpMessageHandler handler)
    {
        return new GeminiKeyProbe(new StubHttpClientFactory(handler), NullLogger<GeminiKeyProbe>.Instance);
    }
}
