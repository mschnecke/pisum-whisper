namespace Pisum.Whisper.Core.Logging;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Debugging;

/// <summary>
/// Registers file logging as the host's logging implementation.
/// </summary>
public static class FileLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Serilog counts the active file within this limit, so ten on disk is nine rolled plus the one
    /// being written.
    /// </summary>
    private const int RetainedFileCountLimit = 10;

    /// <summary>Builds the logger from a peek at the settings file and registers it.</summary>
    public static IServiceCollection AddFileLogging(this IServiceCollection services, out ILogger logger)
    {
        return services.AddFileLogging(FileLoggingOptions.Peek(), out logger);
    }

    public static IServiceCollection AddFileLogging(this IServiceCollection services,
                                                    FileLoggingOptions options,
                                                    out ILogger logger)
    {
#if DEBUG

        // Serilog's own failures are silent by design; a misconfigured sink should not be.
        SelfLog.Enable(message => Debug.WriteLine(message));
#endif

        LogLevelNames.TryParse(options.Config.LogLevel, out var initialLevel);
        var levelSwitch = new LoggingLevelSwitch(initialLevel);
        var monitor = new DroppedLogEventMonitor();

        var directoryFailure = options.Directory.TryCreate();
        var swept = directoryFailure is null
            ? LogRetentionSweep.Run(options.Directory.Path, options.Config.LogRetentionDays)
            : [];

        var configuration = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch);

#if DEBUG
        configuration.WriteTo.Console();
#endif

        // The directory gate applies to the file sink alone, because the file sink is what needs the
        // directory. A log directory that cannot be created is not a reason to refuse to start; it is
        // a reason to say so through the logger that is built anyway.
        var sink = options.SinkOverride ?? (directoryFailure is null ? LogFileSink(options) : null);
        if (sink is not null)
        {
            configuration.WriteTo.Async(sink, options.AsyncBufferSize, monitor: monitor);
        }

        var serilog = configuration.CreateLogger();

        if (directoryFailure is not null)
        {
            serilog.Error(
                "Log directory '{LogDirectory}' is unusable, so nothing is being written to file: {Reason}",
                options.Directory.Path,
                directoryFailure);
        }

        if (swept.Count > 0)
        {
            serilog.Information(
                "Removed {ExpiredLogFileCount} log files older than {LogRetentionDays} days: {ExpiredLogFiles}",
                swept.Count,
                options.Config.LogRetentionDays,
                swept);
        }

        // dispose: false — the caller owns it, and that is load-bearing rather than a detail.
        // Program holds the out parameter below across the whole of startup and disposes it in its
        // own finally, so its one catch can write a Fatal line for a container that never finished
        // building. Putting dispose: true back gives the logger two owners: the container would
        // dispose it at the end of the using that Program's catch runs inside, and the fatal path
        // would silently stop writing anything. FileLoggingRegistrationTests guards it.
        services.AddSerilog(serilog);
        services.AddSingleton(options.Directory);
        services.AddSingleton(levelSwitch);
        services.AddSingleton(monitor);
        services.AddHostedService<FileLoggingHostedService>();

        logger = serilog;
        return services;
    }

    /// <remarks>
    /// Unbuffered: buffering keeps the work on the calling thread once <c>Async</c> is in place, makes
    /// the latency tail worse, and widens the window of events lost when the process dies — which in
    /// a diagnostics feature are the ones worth having.
    /// </remarks>
    private static Action<LoggerSinkConfiguration> LogFileSink(FileLoggingOptions options)
    {
        return write =>
            write.File(
                options.Directory.LogFilePath,
                fileSizeLimitBytes: options.ResolvedFileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCountLimit,
                buffered: false);
    }
}
