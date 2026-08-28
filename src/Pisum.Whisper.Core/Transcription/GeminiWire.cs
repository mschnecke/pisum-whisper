namespace Pisum.Whisper.Core.Transcription;

using System.Text.Json.Serialization;

/// <summary>
/// The Gemini <c>generateContent</c> and <c>models</c> wire shapes, as the reference sends and reads
/// them (<c>ai/gemini.rs:138-230</c>).
/// </summary>
/// <remarks>
/// Every member carries an explicit <see cref="JsonPropertyNameAttribute"/> rather than inheriting a
/// naming policy. The reference's wire shape is mixed — <c>system_instruction</c>, <c>inline_data</c>
/// and <c>mime_type</c> are snake_case while <c>generationConfig</c> and <c>maxOutputTokens</c> are
/// camelCase — so no single policy covers it. Gemini accepts either spelling, so the inconsistency is
/// not reproduced: everything here is camelCase.
/// </remarks>
internal sealed class GeminiRequest
{
    /// <summary>Omitted entirely when absent, which is what the connection test relies on.</summary>
    [JsonPropertyName("systemInstruction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiSystemInstruction? SystemInstruction { get; init; }

    [JsonPropertyName("contents")]
    public required IReadOnlyList<GeminiContent> Contents { get; init; }

    [JsonPropertyName("generationConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiGenerationConfig? GenerationConfig { get; init; }
}

internal sealed class GeminiSystemInstruction
{
    [JsonPropertyName("parts")]
    public required IReadOnlyList<GeminiPart> Parts { get; init; }
}

internal sealed class GeminiContent
{
    [JsonPropertyName("parts")]
    public required IReadOnlyList<GeminiPart> Parts { get; init; }
}

/// <summary>
/// A part is either text or inline data. The reference models this as an untagged enum; one type
/// with two nullable members serialises to the same JSON, because whichever is null is omitted.
/// </summary>
internal sealed class GeminiPart
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("inlineData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiInlineData? InlineData { get; init; }
}

internal sealed class GeminiInlineData
{
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }

    /// <summary>The audio, base64-encoded. Never logged.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

internal sealed class GeminiGenerationConfig
{
    [JsonPropertyName("temperature")]
    public required float Temperature { get; init; }

    [JsonPropertyName("maxOutputTokens")]
    public required int MaxOutputTokens { get; init; }
}

internal sealed class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public IReadOnlyList<GeminiCandidate>? Candidates { get; init; }

    [JsonPropertyName("error")]
    public GeminiError? Error { get; init; }
}

internal sealed class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiCandidateContent? Content { get; init; }
}

internal sealed class GeminiCandidateContent
{
    [JsonPropertyName("parts")]
    public IReadOnlyList<GeminiResponsePart>? Parts { get; init; }
}

internal sealed class GeminiResponsePart
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class GeminiError
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

internal sealed class GeminiModelsResponse
{
    [JsonPropertyName("models")]
    public IReadOnlyList<GeminiModelEntry>? Models { get; init; }
}

internal sealed class GeminiModelEntry
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("supportedGenerationMethods")]
    public IReadOnlyList<string>? SupportedGenerationMethods { get; init; }
}

/// <summary>
/// A model the configured key may use, with the <c>models/</c> prefix already stripped — what
/// change 10's model dropdown binds to.
/// </summary>
public sealed record GeminiModel(string Id, string DisplayName);
