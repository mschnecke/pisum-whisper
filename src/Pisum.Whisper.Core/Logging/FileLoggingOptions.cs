namespace Pisum.Whisper.Core.Logging;

using Pisum.Whisper.Core.Settings;
using Serilog.Configuration;

/// <summary>
/// Provides configuration options for file-based logging, including settings
/// for log file size, asynchronous buffer size, and directory management.
/// </summary>
public sealed class FileLoggingOptions
{
    /// <summary>
    /// Represents the default size of the asynchronous buffer used during file logging operations.
    /// This value determines the maximum number of log entries that can be buffered asynchronously
    /// before being written to the log files.
    /// </summary>
    internal const int DefaultAsyncBufferSize = 10_000;

    /// <summary>
    /// Gets the logging configuration associated with the file logging options.
    /// This configuration includes settings for log level, maximum file size, and retention policies.
    /// </summary>
    public LoggingConfig Config { get; init; } = new();

    /// <summary>
    /// Represents the directory configuration used for logging purposes in file-based logging options.
    /// This property encapsulates the log directory details, including the path where log files are stored
    /// and provides methods for constructing or accessing log file paths.
    /// </summary>
    public LogDirectory Directory { get; init; } = new();

    /// <summary>
    /// Specifies the maximum allowable size, in bytes, for a single log file before it is rolled over.
    /// If set to null, the value is determined by the <see cref="LoggingConfig.LogMaxFileSizeMb"/>
    /// property multiplied by 1 MB.
    /// </summary>
    public long? FileSizeLimitBytes { get; init; }

    /// <summary>
    /// Determines the resolved file size limit, in bytes, for log files. If a specific file size limit
    /// is not provided, this property defaults to the maximum file size defined in the logging configuration.
    /// </summary>
    /// <value>
    /// A <see cref="long"/> value representing the resolved file size limit in bytes. If not explicitly set,
    /// the default value is calculated as the product of the <c>LogMaxFileSizeMb</c> property from the
    /// <see cref="LoggingConfig"/> class and 1 MB (1,048,576 bytes).
    /// </value>
    internal long ResolvedFileSizeLimitBytes => FileSizeLimitBytes ?? Config.LogMaxFileSizeMb * 1024L * 1024L;

    /// <summary>
    /// Gets the size of the buffer used internally for asynchronous log operations.
    /// Controls the maximum number of log events that can be buffered in memory
    /// before being written to the underlying sink.
    /// </summary>
    internal int AsyncBufferSize { get; init; } = DefaultAsyncBufferSize;

    /// <summary>
    /// Gets or sets an optional delegate for overriding the default sink configuration used in logging.
    /// This allows customization of how log events are written by providing a custom action
    /// to modify the <see cref="LoggerSinkConfiguration"/>.
    /// </summary>
    internal Action<LoggerSinkConfiguration>? SinkOverride { get; init; }

    /// <summary>
    /// Retrieves the default logging configuration from the settings and initializes a new instance of FileLoggingOptions.
    /// </summary>
    /// <returns>A new instance of FileLoggingOptions configured using the default logging settings.</returns>
    public static FileLoggingOptions Peek()
    {
        return new FileLoggingOptions {Config = LoggingConfigPeek.Read()};
    }
}
