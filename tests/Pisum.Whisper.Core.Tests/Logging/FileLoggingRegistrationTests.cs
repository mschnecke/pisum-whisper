namespace Pisum.Whisper.Core.Tests.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;
using Serilog.Extensions.Logging;
using Shouldly;
using ILogger = Serilog.ILogger;

[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class FileLoggingRegistrationTests : FileLoggingTestBase
{
    private static FileLoggingOptions Recording(LogDirectory logs, RecordingSink sink, string level = "info")
    {
        return new FileLoggingOptions
        {
            Directory = logs,
            Config = new LoggingConfig {LogLevel = level},
            SinkOverride = write => write.Sink(sink),
        };
    }

    [Fact]
    public void AddFileLogging_ReplacesTheHostDefaultProviders()
    {
        // ILogger<T>.IsEnabled answers "is any provider enabled", so a surviving console provider
        // would report true while Serilog drops the event — and the trace statements change 4 puts
        // in the audio callbacks would do their work for nothing.
        using var host = BuildHost(new FileLoggingOptions {Directory = Logs});

        host.Services.GetRequiredService<ILoggerFactory>().ShouldBeOfType<SerilogLoggerFactory>();
        host.Services.GetRequiredService<ILogger<FileLoggingRegistrationTests>>()
            .IsEnabled(LogLevel.Debug)
            .ShouldBeFalse();
    }

    [Fact]
    public void AddFileLogging_ExposesTheResolvedLogDirectory()
    {
        using var host = BuildHost(new FileLoggingOptions {Directory = Logs});

        host.Services.GetRequiredService<LogDirectory>().Path.ShouldBe(Logs.Path);
        Directory.Exists(Logs.Path).ShouldBeTrue();
    }

    [Fact]
    public void AddFileLogging_WithAnUnusableDirectory_KeepsRunningAndSaysWhy()
    {
        // A file where the directory should be: unusable in the same way on every platform.
        File.WriteAllText(Logs.Path, "not a directory");
        var sink = new RecordingSink();
        var services = new ServiceCollection();

        ILogger? logger = null;
        Should.NotThrow(() => services.AddFileLogging(Recording(Logs, sink), out logger));

        logger.ShouldNotBeNull();
        logger.Information("The application still runs.");
        ((IDisposable) logger).Dispose();

        sink.Messages.ShouldContain(message => message.Contains(Logs.Path) && message.Contains("unusable"));
        sink.Messages.ShouldContain("The application still runs.");
        File.ReadAllText(Logs.Path).ShouldBe("not a directory");
    }

    [Fact]
    public void AddFileLogging_AtInformation_DropsDebugOutput()
    {
        var sink = new RecordingSink();
        var services = new ServiceCollection();
        services.AddFileLogging(Recording(Logs, sink), out var logger);

        logger.Debug("Suppressed by the level switch.");
        logger.Information("Past the level switch.");
        ((IDisposable) logger).Dispose();

        sink.Messages.ShouldBe(["Past the level switch."]);
    }

    /// <summary>
    /// The Serilog logger belongs to whoever called <c>AddFileLogging</c>, not to the container, and
    /// disposing it is what drains the asynchronous sink rather than discarding its queue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the guard on <c>AddSerilog(serilog, false)</c>. The ownership is invisible on the
    /// success path — the logger is disposed either way, a line later — which is exactly why it needs
    /// a test: <c>Program.Main</c>'s one catch writes its <c>Fatal</c> line into this logger for a
    /// container that never finished building, and restoring <c>dispose: true</c> would give the
    /// logger two owners and silently stop the fatal path writing anything.
    /// </para>
    /// <para>
    /// It replaces a test that asserted the host drained the queue. That was true when the container
    /// owned the logger and is not the contract any more; what has to hold now is the two assertions
    /// below.
    /// </para>
    /// </remarks>
    [Fact]
    public void DisposingTheLogger_DrainsTheQueueRatherThanDiscardingIt()
    {
        const int events = 500;
        var services = new ServiceCollection();
        services.AddFileLogging(new FileLoggingOptions {Directory = Logs}, out var logger);

        for (var index = 0; index < events; index++)
        {
            logger.Information("Event {Index}.", index);
        }

        // The Fatal line Program writes on the way out, over a sink that has a queue behind it.
        logger.Fatal(new InvalidOperationException("boom"), "Startup failed: {FailureTitle}", "Startup Error");

        ((IDisposable) logger).Dispose();

        var written = File.ReadAllLines(Logs.LogFilePath);
        written.Count(line => line.Contains("Event ")).ShouldBe(events);
        written.ShouldContain(line => line.Contains("Startup failed: Startup Error"));
    }

    [Fact]
    public void TheContainerDoesNotDisposeTheLogger()
    {
        // Program builds the container inside the try its catch belongs to, so a container that
        // owned the logger would have closed it before the Fatal line was written.
        var services = new ServiceCollection();
        services.AddFileLogging(new FileLoggingOptions {Directory = Logs}, out var logger);

        ((IDisposable) services.BuildServiceProvider()).Dispose();

        logger.Fatal("Written after the container has gone.");
        ((IDisposable) logger).Dispose();

        File.ReadAllText(Logs.LogFilePath).ShouldContain("Written after the container has gone.");
    }
}
