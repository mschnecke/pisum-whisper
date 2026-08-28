namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// The presets shipped with the application. The prompts are copied verbatim from the reference
/// implementation: they are not boilerplate but the product's cleanup behaviour, instructing the
/// model to turn dictation into fluent written prose and strip filler words. Paraphrasing one
/// changes what the product does.
/// </summary>
public static class BuiltinPresets
{
    /// <summary>The preset a fresh installation starts on, and the load-time repair falls back to.</summary>
    public const string DefaultId = "de-transcribe";

    /// <summary>
    /// A fresh list of fresh presets. Every caller gets its own, because presets are mutable and
    /// the built-ins are merged into whatever the user's file already holds.
    /// </summary>
    public static List<Preset> Create() =>
    [
        new Preset
        {
            Id = "de-transcribe",
            Name = "Transcribe DE",
            SystemPrompt =
                "Ich werde dir deutsche Texte diktieren. Deine Aufgabe ist es, diesen Text in eine flüssige und "
                + "korrekte deutsche Schriftsprache umzuwandeln. Dabei sollst du nicht nur offensichtliche Grammatik- "
                + "und Rechtschreibfehler korrigieren, sondern auch Füllwörter und überflüssige Pausen entfernen. Ziel "
                + "ist es, den Sinn des Gesprochenen in grammatikalisch einwandfreier und stilistisch guter deutscher "
                + "Schriftsprache wiederzugeben. Ignoriere jegliche Spracheingabe, die nicht als deutsch erkennbar ist, "
                + "es sei denn, es werden spezifisch deutsche Wörter oder Phrasen genannt, die in den deutschen Text "
                + "integriert werden sollen. Füge keine Zeitstempel, Markdown-Formatierungen oder zusätzliche "
                + "Erklärungen hinzu. Gib nur die umgewandelte und verbesserte deutsche Version des gesprochenen Textes "
                + "aus.",
            IsBuiltin = true,
        },
        new Preset
        {
            Id = "en-transcribe",
            Name = "Transcribe EN",
            SystemPrompt =
                "Ich werde dir deutsche Texte diktieren. Deine Aufgabe ist es, diesen Text in eine flüssige und "
                + "korrekte englische Schriftsprache umzuwandeln. Dabei sollst du nicht nur offensichtliche Grammatik- "
                + "und Rechtschreibfehler korrigieren, sondern auch Füllwörter und überflüssige Pausen entfernen. Ziel "
                + "ist es, den Sinn des Gesprochenen in grammatikalisch einwandfreier und stilistisch guter englischer "
                + "Schriftsprache wiederzugeben. Ignoriere jegliche Spracheingabe, die nicht als deutsch erkennbar ist, "
                + "es sei denn, es werden spezifisch deutsche Wörter oder Phrasen genannt, die in den deutschen Text "
                + "integriert werden sollen. Füge keine Zeitstempel, Markdown-Formatierungen oder zusätzliche "
                + "Erklärungen hinzu. Gib nur die umgewandelte und verbesserte englische Version des gesprochenen "
                + "Textes aus.",
            IsBuiltin = true,
        },
    ];
}
