namespace Pisum.Whisper.App.Tests.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Tests.Logging;
using Shouldly;

/// <summary>
/// Task 1.3 — that the sink linked out of <c>Pisum.Whisper.Core.Tests</c> compiles here and records
/// what the application's own logging registration emits, so the logging-rule assertions in tasks
/// 2.6 and 5.1 run against the same sink the other two suites use rather than a copy of it.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class LinkedRecordingSinkTests : IDisposable
{
    private readonly string _home;

    private readonly RecordingSink _sink = new();

    public LinkedRecordingSinkTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        Directory.Delete(_home, true);
    }

    [Fact]
    public void TheSinkReceivesWhatTheApplicationsLoggingRegistrationEmits()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddFileLogging(
            new FileLoggingOptions
            {
                Directory = new LogDirectory(Path.Combine(_home, "logs")),
                SinkOverride = write => write.Sink(_sink),
            },
            out _);

        using var host = builder.Build();
        host.Services.GetRequiredService<ILogger<LinkedRecordingSinkTests>>()
            .LogInformation("The linked sink is wired.");

        _sink.WaitForMessageContaining("The linked sink is wired.").ShouldBeTrue();
    }
}
