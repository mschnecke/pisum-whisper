namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// A named system prompt the transcription is run under. The identity fields are required rather
/// than defaulted: a preset missing one cannot be defaulted into anything meaningful, so a file
/// containing one is rejected instead of loading an element with empty values.
/// </summary>
public sealed class Preset
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string SystemPrompt { get; set; }

    public bool IsBuiltin { get; set; }
}
