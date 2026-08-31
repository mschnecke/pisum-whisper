namespace Pisum.Whisper.Core.Tests.Logging;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Logging;
using Shouldly;

[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class FileLoggingBufferTests : FileLoggingTestBase
{
    private const int Events = 3000;

    private const int BufferSize = 100;

    private readonly RecordingSink _sink = new() {Delay = TimeSpan.FromMilliseconds(1)};

    private FileLoggingOptions Options()
    {
        return new FileLoggingOptions
        {
            Directory = Logs,
            AsyncBufferSize = BufferSize,
            SinkOverride = write => write.Sink(_sink),
        };
    }

    [Fact]
    public void AFullBufferDropsEventsRatherThanHoldingTheCallingThread()
    {
        // Log backpressure must never become audio backpressure. Measured, blockWhenFull: true holds
        // the calling thread for around 45 seconds over this many events against this sink; the
        // default false returns in tens of milliseconds and drops what it cannot take.
        var services = new ServiceCollection();
        services.AddFileLogging(Options(), out var logger);

        var elapsed = Stopwatch.StartNew();
        for (var index = 0; index < Events; index++)
        {
            logger.Information("Event {Index}.", index);
        }

        elapsed.Stop();

        _sink.Delay = TimeSpan.Zero;
        ((IDisposable) logger).Dispose();

        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        _sink.Count.ShouldBeLessThan(Events, "the buffer has to have overflowed for this to mean anything");
    }

    [Fact]
    public async Task DroppedEventsAreCountedAndReportedAtShutdown()
    {
        // Dropping is the right trade, but a diagnostics subsystem does not get to lose events
        // invisibly.
        using var host = BuildHost(Options());
        var logger = host.Services.GetRequiredService<ILogger<FileLoggingBufferTests>>();

        host.Start();
        for (var index = 0; index < Events; index++)
        {
            logger.LogInformation("Event {Index}.", index);
        }

        _sink.Delay = TimeSpan.Zero;
        var monitor = host.Services.GetRequiredService<DroppedLogEventMonitor>();
        monitor.DroppedMessagesCount.ShouldBeGreaterThan(0);

        // The buffer has to have room again before the shutdown warning can be enqueued: while it is
        // still full the report of the drops would itself be dropped.
        _sink.WaitUntil(() =>
        {
            logger.LogInformation("Buffer drained.");
            return _sink.Messages.Contains("Buffer drained.");
        }).ShouldBeTrue();

        await host.StopAsync(TimeSpan.FromSeconds(10));

        _sink.WaitForMessageContaining("dropped").ShouldBeTrue();
        _sink.Messages.ShouldContain(message => message.Contains($"dropped {monitor.DroppedMessagesCount} events"));
    }
}
