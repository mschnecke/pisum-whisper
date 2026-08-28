using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;
using Shouldly;

namespace Pisum.Whisper.Core.Tests.Logging;

[TestClass]
public sealed class FileLoggingRotationTests : FileLoggingTestBase
{
    /// <summary>Writes <paramref name="events"/> lines and flushes, returning nothing but a drained sink.</summary>
    private static void WriteAndFlush(FileLoggingOptions options, int events)
    {
        var services = new ServiceCollection();
        services.AddFileLogging(options, out var logger);

        for (var index = 0; index < events; index++)
        {
            logger.Information("Event {Index} padded out so that a kilobyte is a handful of lines.", index);
        }

        ((IDisposable)logger).Dispose();
    }

    [TestMethod]
    public void TheLogFileRollsWhenItPassesTheSizeLimit()
    {
        WriteAndFlush(new FileLoggingOptions { Directory = Logs, FileSizeLimitBytes = 1024 }, events: 50);

        LogFiles().Length.ShouldBeGreaterThan(1);
    }

    [TestMethod]
    public void NoMoreThanTenLogFilesAreRetained()
    {
        // Serilog counts the active file within the limit, so ten on disk is nine rolled plus the one
        // being written.
        WriteAndFlush(new FileLoggingOptions { Directory = Logs, FileSizeLimitBytes = 1024 }, events: 2000);

        LogFiles().Length.ShouldBe(10);
    }

    [TestMethod]
    public void AnExpiredActiveLogFileIsSweptBeforeTheSinkOpensIt()
    {
        // Serilog opens the log file with FileShare.Read, which excludes delete. A sweep placed after
        // the sink silently fails against this file, and it is then appended to forever.
        Directory.CreateDirectory(Logs.Path);
        File.WriteAllText(Logs.LogFilePath, "STALE CONTENT FROM AN EARLIER RUN");
        File.SetLastWriteTimeUtc(Logs.LogFilePath, DateTime.UtcNow - TimeSpan.FromDays(30));

        WriteAndFlush(
            new FileLoggingOptions
            {
                Directory = Logs,
                Config = new LoggingConfig { LogRetentionDays = 7 },
            },
            events: 1);

        var written = File.ReadAllText(Logs.LogFilePath);
        written.ShouldNotContain("STALE CONTENT FROM AN EARLIER RUN");
        written.ShouldContain("Removed 1 log files older than 7 days");
    }

    [TestMethod]
    public void AnUnexpiredLogFileIsKept()
    {
        Directory.CreateDirectory(Logs.Path);
        File.WriteAllText(Logs.LogFilePath, "RECENT CONTENT FROM AN EARLIER RUN");
        File.SetLastWriteTimeUtc(Logs.LogFilePath, DateTime.UtcNow - TimeSpan.FromDays(1));

        WriteAndFlush(
            new FileLoggingOptions
            {
                Directory = Logs,
                Config = new LoggingConfig { LogRetentionDays = 7 },
            },
            events: 1);

        File.ReadAllText(Logs.LogFilePath).ShouldContain("RECENT CONTENT FROM AN EARLIER RUN");
    }

    [TestMethod]
    public void WritesDoNotStallTheCallingThreadWhenTheFileRolls()
    {
        // The justification for the asynchronous wrapper is the roll, not throughput: closing the
        // file, opening the next, enumerating the directory and applying retention measures about
        // 1.7 ms at p99.9 on the calling thread, which is what drops audio frames.
        const int events = 10_000;
        var latencies = new double[events];
        var services = new ServiceCollection();
        services.AddFileLogging(
            new FileLoggingOptions { Directory = Logs, FileSizeLimitBytes = 16 * 1024 },
            out var logger);

        for (var index = 0; index < events; index++)
        {
            var start = Stopwatch.GetTimestamp();
            logger.Information("Event {Index} padded out so that the run spans many rolls.", index);
            latencies[index] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        }

        ((IDisposable)logger).Dispose();

        LogFiles().Length.ShouldBeGreaterThan(1, "the measured run has to span a roll");
        Array.Sort(latencies);
        var p999 = latencies[(int)(events * 0.999)];
        p999.ShouldBeLessThan(500d, $"p99.9 was {p999:F1} us against about 1700 us for a synchronous sink");
    }
}
