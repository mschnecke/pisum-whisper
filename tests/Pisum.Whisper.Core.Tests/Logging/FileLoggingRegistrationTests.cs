using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;
using Serilog.Extensions.Logging;
using Shouldly;

namespace Pisum.Whisper.Core.Tests.Logging;

[TestClass]
public sealed class FileLoggingRegistrationTests : FileLoggingTestBase
{
    private static FileLoggingOptions Recording(LogDirectory logs, RecordingSink sink, string level = "info") =>
        new()
        {
            Directory = logs,
            Config = new LoggingConfig { LogLevel = level },
            SinkOverride = write => write.Sink(sink),
        };

    [TestMethod]
    public void AddFileLogging_ReplacesTheHostDefaultProviders()
    {
        // ILogger<T>.IsEnabled answers "is any provider enabled", so a surviving console provider
        // would report true while Serilog drops the event — and the trace statements change 4 puts
        // in the audio callbacks would do their work for nothing.
        using var host = BuildHost(new FileLoggingOptions { Directory = Logs });

        host.Services.GetRequiredService<ILoggerFactory>().ShouldBeOfType<SerilogLoggerFactory>();
        host.Services.GetRequiredService<ILogger<FileLoggingRegistrationTests>>()
            .IsEnabled(LogLevel.Debug)
            .ShouldBeFalse();
    }

    [TestMethod]
    public void AddFileLogging_ExposesTheResolvedLogDirectory()
    {
        using var host = BuildHost(new FileLoggingOptions { Directory = Logs });

        host.Services.GetRequiredService<LogDirectory>().Path.ShouldBe(Logs.Path);
        Directory.Exists(Logs.Path).ShouldBeTrue();
    }

    [TestMethod]
    public void AddFileLogging_WithAnUnusableDirectory_KeepsRunningAndSaysWhy()
    {
        // A file where the directory should be: unusable in the same way on every platform.
        File.WriteAllText(Logs.Path, "not a directory");
        var sink = new RecordingSink();
        var services = new ServiceCollection();

        Serilog.ILogger? logger = null;
        Should.NotThrow(() => services.AddFileLogging(Recording(Logs, sink), out logger));

        logger.ShouldNotBeNull();
        logger.Information("The application still runs.");
        ((IDisposable)logger).Dispose();

        sink.Messages.ShouldContain(message => message.Contains(Logs.Path) && message.Contains("unusable"));
        sink.Messages.ShouldContain("The application still runs.");
        File.ReadAllText(Logs.Path).ShouldBe("not a directory");
    }

    [TestMethod]
    public void AddFileLogging_AtInformation_DropsDebugOutput()
    {
        var sink = new RecordingSink();
        var services = new ServiceCollection();
        services.AddFileLogging(Recording(Logs, sink), out var logger);

        logger.Debug("Suppressed by the level switch.");
        logger.Information("Past the level switch.");
        ((IDisposable)logger).Dispose();

        sink.Messages.ShouldBe(["Past the level switch."]);
    }

    [TestMethod]
    public void DisposingTheHost_DrainsTheQueueRatherThanDiscardingIt()
    {
        // AddSerilog defaults to dispose: false, which throws the queue away instead of draining it.
        // Measured against a clean shutdown, that default leaves an empty file.
        const int events = 500;
        var host = BuildHost(new FileLoggingOptions { Directory = Logs });
        var logger = host.Services.GetRequiredService<ILogger<FileLoggingRegistrationTests>>();

        host.Start();
        for (var index = 0; index < events; index++)
        {
            logger.LogInformation("Event {Index}.", index);
        }

        host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        host.Dispose();

        File.ReadAllLines(Logs.LogFilePath).Count(line => line.Contains("Event ")).ShouldBe(events);
    }
}
