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
    /// <remarks>
    /// <para>
    /// <b>The default has to accept Ogg/Opus, and that is what rules the obvious candidates out.</b>
    /// <c>AppSettings.AudioFormat</c> defaults to <c>Opus</c>, and measured against the live API on
    /// 2026-09-08 every current flash and flash-lite model — the <c>-latest</c> aliases,
    /// <c>gemini-3.1-flash-lite</c>, <c>gemini-3.5-flash</c>, <c>gemini-3.5-flash-lite</c> and
    /// <c>gemini-3.6</c> through <c>-3.8-flash</c> — answers <c>audio/ogg</c> inline data with a bare
    /// <c>400 INVALID_ARGUMENT</c> naming nothing, while accepting the same recording as
    /// <c>audio/wav</c>. No MIME spelling gets round it: <c>audio/opus</c> and
    /// <c>audio/ogg;codecs=opus</c> are refused the same way.
    /// </para>
    /// <para>
    /// This is the only model serving <c>generateContent</c> that takes both the Ogg/Opus audio and
    /// a <c>systemInstruction</c>. <c>gemini-3.5-transcribe</c> takes the audio and then refuses the
    /// instruction ("Developer instruction is not enabled for this model"), which is where every
    /// preset's prompt travels — so a dedicated transcription model is not a drop-in here. The
    /// previous default, <c>gemini-2.5-flash-lite</c>, is now <c>404</c> for any key that has not
    /// used it before.
    /// </para>
    /// </remarks>
    public const string Model = "gemini-3-flash-preview";
}
