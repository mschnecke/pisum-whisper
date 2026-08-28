namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// Represents the configuration settings for a transcription provider,
/// including identity fields such as <c>Id</c> and <c>ApiKey</c>,
/// as well as optional parameters like <c>Model</c> and <c>Enabled</c>.
/// </summary>
public sealed class ProviderConfig
{
    /// <summary>
    /// The unique identifier for the provider configuration. This value is required.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The API key used to authenticate requests with the provider.
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// Specifies the model configuration for the provider. Can be set to a model name or <c>null</c> to allow the provider to use its default model.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Indicates whether the provider is enabled. Defaults to <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
