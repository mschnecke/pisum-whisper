namespace Pisum.Whisper.Core.Dictation;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the recording state machine.
/// </summary>
public static class DictationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DictationOrchestrator"/> once and resolves it under both of its roles.
    /// </summary>
    /// <remarks>
    /// The single registration matters because the orchestrator subscribes to the hotkey in its
    /// constructor: a second instance would be a second subscriber, and one key press would start
    /// two recordings over one microphone. It is a hosted service so that the host constructs it —
    /// nothing else resolves it until change 9's tray icon does — and so that
    /// <see cref="DictationOrchestrator.StopAsync"/> runs on the way out, which is what keeps
    /// quitting mid-delivery from destroying the user's clipboard.
    /// </remarks>
    public static IServiceCollection AddDictationPipeline(this IServiceCollection services)
    {
        services.AddSingleton<DictationOrchestrator>();
        services.AddHostedService(provider => provider.GetRequiredService<DictationOrchestrator>());

        return services;
    }
}
