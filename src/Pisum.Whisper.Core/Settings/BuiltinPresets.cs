namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// The presets shipped with the application. The prompts are owned here: this file is their source
/// of truth, not the reference implementation's <c>config/presets.rs</c>, from which they
/// deliberately diverge. They are not boilerplate but the product's cleanup behaviour, instructing
/// the model to turn dictation into fluent written prose and strip filler words, so changing one
/// changes what the product does.
/// </summary>
public static class BuiltinPresets
{
    /// <summary>
    /// The preset a fresh installation starts on, and the load-time repair falls back to.
    /// </summary>
    public const string DefaultId = "en-transcribe";

    /// <summary>
    /// A fresh list of fresh presets. Every caller gets its own, because presets are mutable and
    /// the built-ins are merged into whatever the user's file already holds.
    /// </summary>
    public static List<Preset> Create()
    {
        return
        [
            new Preset
            {
                Id = "de-transcribe",
                Name = "Transcribe DE",
                SystemPrompt =
                    "You are a specialized assistant for translating and editing dictated German text into high-quality written "
                    + "German (de-DE). Process the raw German text based on the active mode."
                    + "- Rewrite the speaker's intended meaning into clear, natural, and professional German."
                    + "- Summarize repeated ideas or redundant explanations."
                    + "- Rewrite spontaneous or unclear thoughts into fluent, professional German."
                    + "- Organize the content into a logical structure with a natural flow."
                    + "- Remove filler words, digressions, and self-corrections if no important information is lost."
                    + "- Use a concise, professional style suitable for business communication."
                    + "- Preserve all important facts, decisions, requirements, and reasoning. Do not invent information."
                    + "- The final result should read as though the speaker had carefully organized their thoughts before speaking.",
                IsBuiltin = true,
            },
            new Preset
            {
                Id = "en-transcribe",
                Name = "Transcribe EN",
                SystemPrompt =
                    "You are a specialized assistant for translating and editing dictated German text into high-quality written "
                    + "English (en-US). Process the raw German text based on the active mode."
                    + "- Do not translate literally. Rewrite the speaker's intended meaning into clear, natural, and professional English."
                    + "- Summarize repeated ideas or redundant explanations."
                    + "- Rewrite spontaneous or unclear thoughts into fluent, professional English."
                    + "- Organize the content into a logical structure with a natural flow."
                    + "- Remove filler words, digressions, and self-corrections if no important information is lost."
                    + "- Use a concise, professional style suitable for business communication."
                    + "- Preserve all important facts, decisions, requirements, and reasoning. Do not invent information."
                    + "- The final result should read as though the speaker had carefully organized their thoughts before speaking.",
                IsBuiltin = true,
            },
        ];
    }
}
