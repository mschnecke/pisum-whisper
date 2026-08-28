namespace Pisum.Whisper.Core.Logging;

using Serilog.Configuration;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// What the file logger is built from: the peeked <see cref="LoggingConfig"/>, the directory it
/// writes into, and the seams the tests need that no setting exposes.
/// </summary>
public sealed class FileLoggingOptions
{
    internal const int DefaultAsyncBufferSize = 10_000;

    /// <summary>The logging settings as they stood on disk when the process started.</summary>
    public LoggingConfig Config { get; init; } = new();

    public LogDirectory Directory { get; init; } = new();

    /// <summary>
    /// Overrides the roll threshold that <see cref="LoggingConfig.LogMaxFileSizeMb"/> implies. The
    /// tests roll at a kilobyte, which no whole number of megabytes can express.
    /// </summary>
    public long? FileSizeLimitBytes { get; init; }

    internal long ResolvedFileSizeLimitBytes =>
        FileSizeLimitBytes ?? Config.LogMaxFileSizeMb * 1024L * 1024L;

    /// <summary>Lowered by the tests that have to overflow the buffer on purpose.</summary>
    internal int AsyncBufferSize { get; init; } = DefaultAsyncBufferSize;

    /// <summary>
    /// Replaces the rolling file sink the asynchronous wrapper feeds. It exists so the tests can put
    /// a sink they control behind the same wrapper the application uses, rather than reconstructing
    /// the wrapper and testing their own copy of it.
    /// </summary>
    internal Action<LoggerSinkConfiguration>? SinkOverride { get; init; }

    /// <summary>Reads <see cref="LoggingConfig"/> from the settings file without opening the container.</summary>
    public static FileLoggingOptions Peek() => new() { Config = LoggingConfigPeek.Read() };
}
