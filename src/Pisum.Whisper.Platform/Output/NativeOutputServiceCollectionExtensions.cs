namespace Pisum.Whisper.Platform.Output;

using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Pisum.Whisper.Core.Output;

/// <summary>
/// Registers the native half of the text output: the clipboard and the paste probe.
/// </summary>
/// <remarks>
/// Registered separately from Core's <c>AddTextOutput</c> on purpose. <c>Program.cs</c> builds its
/// container with <c>ValidateOnBuild</c>, so forgetting this half is a startup failure that names
/// <see cref="ISystemClipboard"/> rather than a null reference at the first paste.
/// </remarks>
public static class NativeOutputServiceCollectionExtensions
{
    public static IServiceCollection AddNativeOutput(this IServiceCollection services)
    {
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ISystemClipboard, WindowsClipboard>();
            services.AddSingleton<IPasteProbe, WindowsPasteProbe>();

            return services;
        }

        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<ISystemClipboard, MacOsClipboard>();
            services.AddSingleton<IPasteProbe, MacOsPasteProbe>();

            return services;
        }

        // Only win-x64 and osx-arm64 are shipped, and there is no meaningful clipboard to fall back
        // to on anything else.
        throw new PlatformNotSupportedException(
            $"Text output is implemented for Windows and macOS only; this is {RuntimeInformation.OSDescription}.");
    }
}
