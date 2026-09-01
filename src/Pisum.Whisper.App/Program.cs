namespace Pisum.Whisper.App;

using Avalonia;
using Avalonia.Controls;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Pisum.Whisper.Platform.Output;
using Pisum.Whisper.Platform.Shell;

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

        // A singleton, because it is cache-authoritative: it reads the file once, and every later
        // read is served from memory.
        builder.Services.AddSingleton<SettingsStore>();

        builder.Services.AddAudioPipeline();

        builder.Services.AddGeminiTranscription();

        // One hook for the whole process; it starts with the host, before Avalonia's run loop, and
        // needs no UI of its own.
        builder.Services.AddGlobalHotkey();

        // Two halves on purpose: the sequence and its rules are in Core, the clipboard and the paste
        // probe are native and live in Platform. Registering them separately is what makes a missing
        // native half a named startup failure rather than a null reference at the first paste.
        builder.Services.AddTextOutput();
        builder.Services.AddNativeOutput();

        // The settings window's Open Log Folder button, and the only thing in this application that
        // asks the operating system to show the user a directory.
        builder.Services.AddNativeShell();

        // The settings window and the one edit-and-persist helper its six tabs share. Both are
        // singletons: the window is created on first open and kept, and one editor is what lets the
        // Presets tab's flush cover a draft another tab left pending.
        builder.Services.AddSingleton<SettingsEditor>();
        builder.Services.AddSingleton<SettingsWindowViewModel>();

        // Last, because it consumes all four of the capabilities above. Registered as a hosted
        // service so that its StopAsync runs on the way out: a dictation caught mid-delivery has to
        // finish putting the user's clipboard back before the process exits. App resolves the same
        // singleton once Avalonia is up, and drives the tray icon from its StateChanged.
        builder.Services.AddDictationPipeline();

        try
        {
            return builder.Build();
        }
        catch (Exception exception)
        {
            logger.Fatal(exception, "The service container could not be built.");

            // Nothing else will dispose of it: the container that would have owned it does not exist.
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

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services)
    {
        return AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions {ShowInDock = false})
            .LogToTrace();
    }
}
