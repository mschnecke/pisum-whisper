namespace Pisum.Whisper.Platform.Shell;

using Microsoft.Extensions.DependencyInjection;
using Pisum.Whisper.Core.Shell;

/// <summary>
/// Registers the shell seam, beside <c>AddNativeOutput</c>.
/// </summary>
public static class NativeShellServiceCollectionExtensions
{
    public static IServiceCollection AddNativeShell(this IServiceCollection services)
    {
        services.AddSingleton<ISystemShell, SystemShell>();

        return services;
    }
}
