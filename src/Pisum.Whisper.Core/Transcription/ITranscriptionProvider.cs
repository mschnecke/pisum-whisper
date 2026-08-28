namespace Pisum.Whisper.Core.Transcription;

using Pisum.Whisper.Core.Audio;

/// <summary>
/// Turns encoded audio into text. The one seam the dictation pipeline depends on — both a single
/// keyed provider and the pool that fans out across several implement it, so a caller never learns
/// which it holds.
/// </summary>
public interface ITranscriptionProvider
{
    /// <summary>Transcribes <paramref name="audio"/> under <paramref name="systemPrompt"/>.</summary>
    /// <remarks>
    /// The prompt is passed in rather than resolved here: the active preset always resolves
    /// (guaranteed by the <c>settings-persistence</c> capability), so the reference's hardcoded
    /// fallback prompt has nothing to guard against, and the caller already holds the settings.
    /// </remarks>
    /// <exception cref="TranscriptionException">
    /// No transcript could be produced; <see cref="TranscriptionException.Category"/> says why.
    /// </exception>
    Task<string> TranscribeAsync(EncodedAudio audio, string systemPrompt, CancellationToken cancellationToken);
}
