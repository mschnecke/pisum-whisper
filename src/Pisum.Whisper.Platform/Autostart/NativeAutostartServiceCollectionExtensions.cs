namespace Pisum.Whisper.Platform.Autostart;

using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Pisum.Whisper.Core.Autostart;

/// <summary>
/// Registers the native half of autostart: the machine's own login-item mechanism.
/// </summary>
/// <remarks>
/// Registered separately from Core's <c>AddAutostart</c>, in the shape of <c>AddNativeOutput</c>.
/// <c>Program.cs</c> builds its container with <c>ValidateOnBuild</c>, so forgetting this half is a
/// startup failure that names <see cref="IAutostartService"/> rather than a null reference at the
/// first save.
/// </remarks>
public static class NativeAutostartServiceCollectionExtensions
{
    public static IServiceCollection AddNativeAutostart(this IServiceCollection services)
    {
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IAutostartService, WindowsAutostart>();

            return services;
        }

        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IAutostartService, MacOsAutostart>();

            return services;
        }

        // Only win-x64 and osx-arm64 are shipped, and there is no meaningful login item to fall back
        // to on anything else.
        throw new PlatformNotSupportedException(
            $"Start at login is implemented for Windows and macOS only; this is {RuntimeInformation.OSDescription}.");
    }
}
