namespace Pisum.Whisper.Core.Transcription;

/// <summary>
/// The public home of the constants a Gemini caller outside this assembly has to name.
/// </summary>
/// <remarks>
/// <see cref="GeminiProvider"/> is <c>internal</c> behind <see cref="GeminiProviderPool"/> on
/// purpose, so its <c>DefaultModel</c> cannot be read from the settings window — which needs it for
/// the model dropdown's "Default (...)" option. Giving the constant one public home is the smaller
/// change than widening the provider, and the same answer change 8 gave for the sample rate.
/// </remarks>
public static class GeminiDefaults
{
    /// <summary>The model used when a provider entry names none.</summary>
    public const string Model = "gemini-2.5-flash-lite";
}
