namespace Pisum.Whisper.Core.Output;

using Microsoft.Extensions.DependencyInjection;
using SharpHook.Providers;
using SharpHook.Simulation;

/// <summary>
/// Registers the delivery sequence and the event simulator it pastes with.
/// </summary>
/// <remarks>
/// The native half — <see cref="ISystemClipboard"/> and <see cref="IPasteProbe"/> — is registered
/// separately by <c>AddNativeOutput</c> in <c>Pisum.Whisper.Platform</c>, so that omitting it is a
/// <c>ValidateOnBuild</c> failure naming the missing service rather than a null reference at the
/// first paste.
/// </remarks>
public static class TextOutputServiceCollectionExtensions
{
    public static IServiceCollection AddTextOutput(this IServiceCollection services)
    {
        // SharpHook's own interface is the seam; there is no wrapper, because wrapping it would be
        // an abstraction over an abstraction whose only purpose is a test double that already ships
        // in SharpHook.Testing. The simulator is disposable and lives as long as the process, so the
        // container owns it.
        services.AddSingleton<IEventSimulator>(_ =>
            EventSimulator.Create("Pisum Whisper", UioHookProvider.Instance));

        services.AddSingleton<ITextOutput, TextOutput>();

        return services;
    }
}
