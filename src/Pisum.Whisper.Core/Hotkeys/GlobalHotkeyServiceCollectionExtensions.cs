namespace Pisum.Whisper.Core.Hotkeys;

using Microsoft.Extensions.DependencyInjection;
using SharpHook.Data;
using SharpHook.Logging;

/// <summary>
/// Registers the global hotkey observation.
/// </summary>
public static class GlobalHotkeyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="GlobalHotkeyService"/> once and resolves it under all three of its roles.
    /// </summary>
    /// <remarks>
    /// The single registration is what enforces libuiohook's constraint that only one global hook may
    /// run per process: they share one static native callback, and a second concurrent hook corrupts
    /// its internal state. Change 10's hotkey recorder therefore reuses this instance through
    /// <see cref="IGlobalHotkeyService.CaptureAsync"/> rather than constructing a hook of its own.
    /// </remarks>
    public static IServiceCollection AddGlobalHotkey(this IServiceCollection services)
    {
        // Warning and above: libuiohook logs per event at the lower levels, and this application does
        // not put keystrokes in a log file. The service filters again on the same rule, so the
        // guarantee does not rest on this argument alone.
        services.AddSingleton<ILogSource>(_ => LogSource.RegisterOrGet(LogLevel.Warn));

        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<IGlobalHotkeyService>(provider => provider.GetRequiredService<GlobalHotkeyService>());
        services.AddHostedService(provider => provider.GetRequiredService<GlobalHotkeyService>());

        return services;
    }
}
