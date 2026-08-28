namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// Credentials for one transcription provider. As in <see cref="Preset"/>, the identity fields are
/// required and the rest default.
/// </summary>
public sealed class ProviderConfig
{
    public required string Id { get; set; }

    public required string ApiKey { get; set; }

    /// <summary>The provider's model name, or <c>null</c> to let the provider choose its default.</summary>
    public string? Model { get; set; }

    public bool Enabled { get; set; } = true;
}
