using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Settings;

namespace Pisum.Whisper.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var host = BuildHost(args);
        host.Start();

        // Settings are read once, before the UI exists, so a corrupt file fails at startup naming
        // the file rather than at first use of whatever happened to read it first.
        LoadSettings(host.Services);

        try
        {
            return BuildAvaloniaApp(host.Services)
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Validating on build turns an unsatisfiable registration into a startup failure that names
        // the offending service, rather than a null reference at first use.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

        // A singleton, because it is cache-authoritative: it reads the file once and every later
        // read is served from memory.
        builder.Services.AddSingleton<SettingsStore>();

        return builder.Build();
    }

    private static void LoadSettings(IServiceProvider services)
    {
        var store = services.GetRequiredService<SettingsStore>();
        store.Load();

        services.GetRequiredService<ILogger<SettingsStore>>().LogInformation(
            "Settings loaded from {Path} (first launch: {IsFirstLaunch}).",
            store.FilePath,
            store.IsFirstLaunch);
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
