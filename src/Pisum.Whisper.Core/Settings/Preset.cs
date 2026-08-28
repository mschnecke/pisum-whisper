namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// Represents a predefined configuration for transcription prompts
/// in the system. Includes necessary identity fields to ensure
/// meaningful processing without defaulting incomplete definitions.
/// Presets with missing identity fields are rejected during loading.
/// </summary>
public sealed class Preset
{
    /// <summary>
    /// The unique identifier for the preset. This property is required,
    /// as the absence of a valid identifier prevents the preset from being meaningfully
    /// used or loaded, leading to its rejection.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The name of the preset. This property represents a human-readable identifier
    /// for the preset and is required for maintaining a meaningful and identifiable preset configuration.
    /// A missing or empty value for this property will result in rejection of the preset during loading.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The system prompt associated with the preset.
    /// This prompt defines the specific behavior or instructions the transcription system
    /// should follow when processing input. Each prompt is a required field and must be explicitly
    /// provided, as missing values would render the preset unusable or non-meaningful.
    /// </summary>
    public required string SystemPrompt { get; set; }

    /// <summary>
    /// Indicates whether the preset is a built-in system preset.
    /// Built-in presets are predefined within the system and cannot
    /// be removed or modified as non-built-in presets.
    /// </summary>
    public bool IsBuiltin { get; init; }
}
