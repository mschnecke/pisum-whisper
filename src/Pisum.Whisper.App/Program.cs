using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;

namespace Pisum.Whisper.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var host = BuildHost(args);

        // Settings are read once, before the UI exists, so a corrupt file fails at startup naming
        // the file rather than at first use of whatever happened to read it first. It runs before
        // the host starts because the log level switch takes its initial value from the loaded
        // settings when the hosted services start.
        LoadSettings(host.Services);

        host.Start();

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

        // Before the container, so that the validation failure above is written to the log file.
        // This process has no console in a release build, so stderr is nowhere.
        builder.Services.AddFileLogging(out var logger);

        // A singleton, because it is cache-authoritative: it reads the file once and every later
        // read is served from memory.
        builder.Services.AddSingleton<SettingsStore>();

        // One hook for the whole process; it starts with the host, before Avalonia's run loop, and
        // needs no UI of its own.
        builder.Services.AddGlobalHotkey();

        try
        {
            return builder.Build();
        }
        catch (Exception exception)
        {
            logger.Fatal(exception, "The service container could not be built.");

            // Nothing else will dispose it: the container that would have owned it does not exist.
            // The asynchronous sink discards its queue rather than draining it if it is not disposed.
            (logger as IDisposable)?.Dispose();
            throw;
        }
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
