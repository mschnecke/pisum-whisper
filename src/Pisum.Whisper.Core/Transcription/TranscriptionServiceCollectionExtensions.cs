namespace Pisum.Whisper.Core.Transcription;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Gemini transcription client and the settings window's key probe.
/// </summary>
public static class TranscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddGeminiTranscription(this IServiceCollection services)
    {
        services.AddHttpClient(GeminiHttpClient.Name, client =>
        {
            client.BaseAddress = GeminiHttpClient.BaseAddress;
            client.Timeout = GeminiHttpClient.Timeout;
        });

        // The pool is the contract: it implements ITranscriptionProvider itself, so the dictation
        // pipeline depends on one seam and never learns how many keys are behind it.
        services.AddSingleton<ITranscriptionProvider, GeminiProviderPool>();
        services.AddSingleton<IGeminiKeyProbe, GeminiKeyProbe>();

        return services;
    }
}
