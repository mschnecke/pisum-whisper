namespace Pisum.Whisper.Core.Tests.Transcription;

using System.Text.Json;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Tasks 3.1-3.3 — the wire shape, serialised through the source-generated context rather than
/// reflection, so a property name that drifts fails here rather than at the API.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class GeminiWireTests
{
    [Fact]
    public void ARequestWithoutASystemInstruction_OmitsTheProperty()
    {
        var request = new GeminiRequest
        {
            Contents = [new GeminiContent {Parts = [new GeminiPart {Text = "Respond with only: OK"}]}],
            GenerationConfig = new GeminiGenerationConfig {Temperature = 0.1f, MaxOutputTokens = 10},
        };

        var json = JsonSerializer.Serialize(request, GeminiJsonContext.Default.GeminiRequest);

        json.ShouldNotContain("systemInstruction");
        json.ShouldContain("\"text\":\"Respond with only: OK\"");
        json.ShouldNotContain("inlineData");
    }

    [Fact]
    public void APopulatedRequest_EmitsEveryPropertyGeminiExpects()
    {
        var request = new GeminiRequest
        {
            SystemInstruction = new GeminiSystemInstruction
            {
                Parts = [new GeminiPart {Text = "Transcribe the audio."}],
            },
            Contents =
            [
                new GeminiContent
                {
                    Parts =
                    [
                        new GeminiPart
                        {
                            InlineData = new GeminiInlineData {MimeType = "audio/ogg", Data = "AQID"},
                        },
                    ],
                },
            ],
            GenerationConfig = new GeminiGenerationConfig {Temperature = 0.1f, MaxOutputTokens = 8192},
        };

        var json = JsonSerializer.Serialize(request, GeminiJsonContext.Default.GeminiRequest);

        json.ShouldContain("\"systemInstruction\"");
        json.ShouldContain("\"inlineData\"");
        json.ShouldContain("\"mimeType\":\"audio/ogg\"");
        json.ShouldContain("\"data\":\"AQID\"");
        json.ShouldContain("\"generationConfig\"");
        json.ShouldContain("\"maxOutputTokens\":8192");

        // The text member of the inline-data part is null and must not be emitted as "text":null.
        json.ShouldNotContain("null");
    }

    [Fact]
    public void AResponse_YieldsTheCandidateText()
    {
        const string payload = """
                               {
                                 "candidates": [
                                   { "content": { "parts": [ { "text": "hello world" } ], "role": "model" } }
                                 ],
                                 "usageMetadata": { "totalTokenCount": 42 }
                               }
                               """;

        var response = JsonSerializer.Deserialize(payload, GeminiJsonContext.Default.GeminiResponse);

        response.ShouldNotBeNull();
        response.Error.ShouldBeNull();
        response.Candidates!.Single().Content!.Parts!.Single().Text.ShouldBe("hello world");
    }

    [Fact]
    public void AnErrorResponse_YieldsTheMessage()
    {
        const string payload = """
                               { "error": { "code": 400, "message": "API key not valid", "status": "INVALID_ARGUMENT" } }
                               """;

        var response = JsonSerializer.Deserialize(payload, GeminiJsonContext.Default.GeminiResponse);

        response.ShouldNotBeNull();
        response.Candidates.ShouldBeNull();
        response.Error!.Message.ShouldBe("API key not valid");
    }

    [Fact]
    public void AModelsResponse_YieldsNamesAndSupportedMethods()
    {
        const string payload = """
                               {
                                 "models": [
                                   {
                                     "name": "models/gemini-2.5-flash-lite",
                                     "displayName": "Gemini 2.5 Flash-Lite",
                                     "supportedGenerationMethods": [ "generateContent", "countTokens" ]
                                   },
                                   {
                                     "name": "models/embedding-001",
                                     "displayName": "Embedding 001",
                                     "supportedGenerationMethods": [ "embedContent" ]
                                   }
                                 ]
                               }
                               """;

        var response = JsonSerializer.Deserialize(payload, GeminiJsonContext.Default.GeminiModelsResponse);

        response.ShouldNotBeNull();
        response.Models!.Count.ShouldBe(2);
        response.Models[0].Name.ShouldBe("models/gemini-2.5-flash-lite");
        response.Models[0].SupportedGenerationMethods!.ShouldContain("generateContent");
        response.Models[1].SupportedGenerationMethods!.ShouldNotContain("generateContent");
    }
}
