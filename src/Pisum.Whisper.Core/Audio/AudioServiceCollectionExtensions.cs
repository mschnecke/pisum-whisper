namespace Pisum.Whisper.Core.Audio;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the audio capture and encoding pipeline.
/// </summary>
public static class AudioServiceCollectionExtensions
{
    public static IServiceCollection AddAudioPipeline(this IServiceCollection services)
    {
        services.AddSingleton<IAudioCapture, MiniAudioCapture>();
        services.AddSingleton<IAudioEncoder, AudioEncoder>();
        return services;
    }
}
