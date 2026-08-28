namespace Pisum.Whisper.Core.Transcription;

/// <summary>
/// Why a transcription failed, fixed where the failure is raised so a caller can choose how to
/// present it without inspecting message text.
/// </summary>
/// <remarks>
/// The reference decides notification titles by substring-matching the error message
/// (<c>hotkey/manager.rs:488-515</c>), but only inside its <c>AppError::Transcription</c> arm —
/// its <c>Audio</c> and <c>Output</c> arms are already matched by type. There are therefore no
/// <c>Audio</c> or <c>Output</c> members here: <see cref="Audio.AudioException"/> and change 7's
/// output error carry those distinctions themselves, and every member below is one this capability
/// actually raises.
/// </remarks>
public enum ErrorCategory
{
    /// <summary>Nothing is configured to transcribe with, or the request cannot be sent as configured.</summary>
    Configuration,

    /// <summary>The request never reached Gemini: a transport failure or a timeout.</summary>
    Network,

    /// <summary>Gemini rejected the API key.</summary>
    Authentication,

    /// <summary>Gemini refused the request for quota or rate reasons.</summary>
    RateLimit,

    /// <summary>Gemini was reached and answered, but no usable transcript came back.</summary>
    Transcription,
}
