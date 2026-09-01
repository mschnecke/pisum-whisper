namespace Pisum.Whisper.Core.Autostart;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the reconciler that keeps the login registration true of the machine.
/// </summary>
/// <remarks>
/// The native half — <see cref="IAutostartService"/> — is registered separately by
/// <c>AddNativeAutostart</c> in <c>Pisum.Whisper.Platform</c>, in the shape of <c>AddTextOutput</c>
/// plus <c>AddNativeOutput</c>, so that omitting it is a <c>ValidateOnBuild</c> failure naming the
/// missing service.
/// </remarks>
public static class AutostartServiceCollectionExtensions
{
    public static IServiceCollection AddAutostart(this IServiceCollection services)
    {
        services.AddSingleton<AutostartReconciler>();
        services.AddHostedService(provider => provider.GetRequiredService<AutostartReconciler>());

        return services;
    }
}
