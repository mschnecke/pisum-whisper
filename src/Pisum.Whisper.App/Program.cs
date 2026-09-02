namespace Pisum.Whisper.App;

using Avalonia;
using Avalonia.Controls;
using Pisum.Whisper.App.Notifications;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Autostart;
using Pisum.Whisper.Core.Diagnostics;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Notifications;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Pisum.Whisper.Platform.Autostart;
using Pisum.Whisper.Platform.Diagnostics;
using Pisum.Whisper.Platform.Output;
using Pisum.Whisper.Platform.Shell;
using ILogger = Serilog.ILogger;

internal static class Program
{
    /// <summary>
    /// Starts the application, and reports on screen anything that stops it starting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four failures happen before this process has any surface of its own — the container failing
    /// <c>ValidateOnBuild</c>, a settings file that cannot be parsed or cannot be written, and a tray
    /// asset that will not load. A tray-only process that dies at any of them is indistinguishable
    /// from one that never launched, which is why the reporter is constructed on the first line: it
    /// is the only thing here that cannot itself fail to exist.
    /// </para>
    /// <para>
    /// <b><c>using var host</c> must not go inside the <c>try</c>.</b> A <c>using var</c> releases at
    /// the end of its own block, <em>before</em> the matching <c>catch</c> runs, which would hand the
    /// catch a disposed host. The host is disposed in the <c>finally</c> instead, after the catch has
    /// had its use of it.
    /// </para>
    /// </remarks>
    [STAThread]
    public static int Main(string[] args)
    {
        var reporter = NativeFatalErrorReporter.Create();
        ILogger? logger = null;
        IHost? host = null;

        try
        {
            host = BuildHost(args, out logger);

            // Settings are read once, before the UI exists, so a corrupt file fails at startup
            // naming the file rather than at first use of whatever happened to read it first. It
            // runs before the host starts because the log level switch takes its initial value from
            // the loaded settings when the hosted services start.
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
        catch (Exception exception)
        {
            // Where the log would be, rather than where it is: an unusable log directory and a fatal
            // failure can coincide, and this cannot fail while composing a message about a failure.
            var (title, message) = StartupFailure.Describe(
                exception,
                Path.Combine(LogDirectory.DefaultPath(), LogDirectory.LogFileName));

            logger?.Fatal(exception, "Startup failed: {FailureTitle}", title);

            // Before the dialog, not after. The asynchronous sink drops its queue rather than
            // draining it if it is never disposed, and the dialog blocks until the user dismisses
            // it — which at login may be a long time, or never.
            (logger as IDisposable)?.Dispose();
            logger = null;

            reporter.Report(title, message);

            // Returned rather than rethrown: letting it leave Main would put the operating system's
            // own crash dialog on top of the one just shown.
            return 1;
        }
        finally
        {
            host?.Dispose();
            (logger as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Builds the host, handing back the Serilog logger it registered.
    /// </summary>
    /// <remarks>
    /// <paramref name="logger"/> is assigned immediately after <c>AddFileLogging</c> and before
    /// anything that can throw, so the caller's local is written even when <c>builder.Build()</c>
    /// fails afterwards. That is what lets one catch in <see cref="Main"/> log all four fatal cases.
    /// There is deliberately <b>no</b> catch of its own here: with the assignment above the caller's
    /// catch already logs, disposes and reports, and keeping both would write two <c>Fatal</c> lines
    /// and dispose the logger twice.
    /// </remarks>
    private static IHost BuildHost(string[] args, out ILogger logger)
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
        builder.Services.AddFileLogging(out var serilog);

        // Assigned here, before anything below can throw. Main's catch owns this logger from now on.
        logger = serilog;

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

        // Two halves, for the third time: the reconciling is in Core and the registry key and the
        // LaunchAgent plist are native. The reconciler is a hosted service, so the login
        // registration is brought into agreement with the setting at startup — before Avalonia
        // exists, which is why the first-launch flow in App has nothing left to do about it.
        builder.Services.AddAutostart();
        builder.Services.AddNativeAutostart();

        // The settings window's Open Log Folder button, and the only thing in this application that
        // asks the operating system to show the user a directory.
        builder.Services.AddNativeShell();

        // And again: the forced-versus-suppressible policy is in Core, while the window it is drawn
        // as is Avalonia and belongs here beside the tray icon. Omitting the
        // presenter is then a startup failure naming INotificationPresenter rather than a null
        // reference at the first error a user hits. The concrete type is registered as well so that
        // App.OnExit can close what is still on screen without casting the interface.
        builder.Services.AddNotifications();
        builder.Services.AddSingleton<ToastPresenter>();
        builder.Services.AddSingleton<INotificationPresenter>(
            provider => provider.GetRequiredService<ToastPresenter>());

        // The settings window and the one edit-and-persist helper its six tabs share. Both are
        // singletons: the window is created on first open and kept, and one editor is what lets the
        // Presets tab's flush cover a draft another tab left pending.
        builder.Services.AddSingleton<SettingsEditor>();
        builder.Services.AddSingleton<SettingsWindowViewModel>();

        // Last, because it consumes all five of the capabilities above. Registered as a hosted
        // service so that its StopAsync runs on the way out: a dictation caught mid-delivery has to
        // finish putting the user's clipboard back before the process exits. App resolves the same
        // singleton once Avalonia is up, and drives the tray icon from its StateChanged.
        builder.Services.AddDictationPipeline();

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

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services)
    {
        return AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions {ShowInDock = false})
            .LogToTrace();
    }
}
