namespace Pisum.Whisper.Core.Logging;

using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;

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
    public static IServiceCollection AddFileLogging(this IServiceCollection services, out ILogger logger) =>
        services.AddFileLogging(FileLoggingOptions.Peek(), out logger);

    /// <summary>
    /// Builds the logger from <paramref name="options"/> and registers it through
    /// <c>AddSerilog</c>, which replaces <c>ILoggerFactory</c> outright so none of the host's
    /// default providers survive alongside Serilog.
    /// </summary>
    /// <param name="logger">
    /// The logger just built. The caller gets it back so that a failure of <c>builder.Build()</c>
    /// itself can be logged — which is the whole reason logging is configured before the container.
    /// </param>
    /// <remarks>
    /// The order matters and is load-bearing: resolve the directory, create it, sweep it, then open
    /// the sink. Serilog holds the active file with <see cref="FileShare.Read"/>, which excludes
    /// delete, so a sweep placed after the sink silently fails against the one file most likely to
    /// be expired — the one it is about to append to.
    /// </remarks>
    public static IServiceCollection AddFileLogging(
        this IServiceCollection services,
        FileLoggingOptions options,
        out ILogger logger)
    {
#if DEBUG
        // Serilog's own failures are silent by design; a misconfigured sink should not be.
        Serilog.Debugging.SelfLog.Enable(message => System.Diagnostics.Debug.WriteLine(message));
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
            configuration.WriteTo.Async(sink, bufferSize: options.AsyncBufferSize, monitor: monitor);
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

        services.AddSerilog(serilog, dispose: true);
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
    private static Action<LoggerSinkConfiguration> LogFileSink(FileLoggingOptions options) => write =>
        write.File(
            options.Directory.LogFilePath,
            fileSizeLimitBytes: options.ResolvedFileSizeLimitBytes,
            rollOnFileSizeLimit: true,
            retainedFileCountLimit: RetainedFileCountLimit,
            buffered: false);
}
