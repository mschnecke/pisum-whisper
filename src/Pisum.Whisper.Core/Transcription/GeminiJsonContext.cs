namespace Pisum.Whisper.Core.Transcription;

using System.Text.Json.Serialization;

/// <summary>
/// The source-generated context the Gemini wire types are serialised through, following
/// <see cref="Settings.SettingsJsonContext"/>'s pattern so trimming and AOT stay open.
/// </summary>
/// <remarks>
/// No naming policy is set: every member of every type in <c>GeminiWire.cs</c> carries an explicit
/// <see cref="JsonPropertyNameAttribute"/>, because the reference's wire shape mixes snake_case and
/// camelCase and no single policy covers it.
/// </remarks>
[JsonSerializable(typeof(GeminiRequest))]
[JsonSerializable(typeof(GeminiResponse))]
[JsonSerializable(typeof(GeminiModelsResponse))]
internal sealed partial class GeminiJsonContext : JsonSerializerContext;
