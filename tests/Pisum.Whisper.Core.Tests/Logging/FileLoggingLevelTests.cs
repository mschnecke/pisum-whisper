using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;
using Serilog.Core;
using Shouldly;

namespace Pisum.Whisper.Core.Tests.Logging;

[TestClass]
public sealed class FileLoggingLevelTests : FileLoggingTestBase
{
    private readonly RecordingSink _sink = new();

    private FileLoggingOptions Options() => new()
    {
        Directory = Logs,
        Config = LoggingConfigPeek.Read(SettingsPath),
        SinkOverride = write => write.Sink(_sink),
    };

    private void WriteSettings(string logLevel)
    {
        File.WriteAllText(SettingsPath, $$"""{ "loggingConfig": { "logLevel": "{{logLevel}}" } }""");
    }

    [TestMethod]
    public void ChangingTheLevelInSettings_TakesEffectWithoutRebuildingTheLogger()
    {
        // The point of the feature: restarting to raise verbosity destroys the state that caused the
        // problem being reproduced.
        WriteSettings("info");
        using var host = BuildHost(Options());
        var store = host.Services.GetRequiredService<SettingsStore>();
        var logger = host.Services.GetRequiredService<ILogger<FileLoggingLevelTests>>();

        store.Load();
        host.Start();

        logger.LogDebug("Before the change.");
        logger.LogInformation("Marker.");
        _sink.WaitForMessageContaining("Marker.").ShouldBeTrue();
        _sink.Messages.ShouldNotContain("Before the change.");

        var settings = store.Current;
        settings.LoggingConfig.LogLevel = "debug";
        store.Save(settings);

        logger.LogDebug("After the change.");

        _sink.WaitForMessageContaining("After the change.").ShouldBeTrue();
    }

    [TestMethod]
    public void RaisingTheLevel_MovesIsEnabledWithTheSwitchRatherThanBehindASecondGate()
    {
        // A stray provider, or an ILoggerFactory that was not replaced, would answer IsEnabled on its
        // own level and defeat the switch silently.
        WriteSettings("info");
        using var host = BuildHost(Options());
        var store = host.Services.GetRequiredService<SettingsStore>();
        var logger = host.Services.GetRequiredService<ILogger<FileLoggingLevelTests>>();
        var levelSwitch = host.Services.GetRequiredService<LoggingLevelSwitch>();

        store.Load();
        host.Start();

        levelSwitch.MinimumLevel.ShouldBe(Serilog.Events.LogEventLevel.Information);
        logger.IsEnabled(LogLevel.Debug).ShouldBeFalse();

        var settings = store.Current;
        settings.LoggingConfig.LogLevel = "debug";
        store.Save(settings);

        levelSwitch.MinimumLevel.ShouldBe(Serilog.Events.LogEventLevel.Debug);
        logger.IsEnabled(LogLevel.Debug).ShouldBeTrue();
    }

    [TestMethod]
    public void LoweringTheLevel_StopsOutputWithoutARestart()
    {
        WriteSettings("debug");
        using var host = BuildHost(Options());
        var store = host.Services.GetRequiredService<SettingsStore>();
        var logger = host.Services.GetRequiredService<ILogger<FileLoggingLevelTests>>();

        store.Load();
        host.Start();

        logger.LogDebug("Before the change.");
        _sink.WaitForMessageContaining("Before the change.").ShouldBeTrue();

        var settings = store.Current;
        settings.LoggingConfig.LogLevel = "warn";
        store.Save(settings);

        logger.LogInformation("Suppressed.");
        logger.LogWarning("Marker.");

        _sink.WaitForMessageContaining("Marker.").ShouldBeTrue();
        _sink.Messages.ShouldNotContain("Suppressed.");
    }

    [TestMethod]
    public void AtTrace_NothingClampsTheSwitchFromAbove()
    {
        // Microsoft.Extensions.Logging installs a provider-scoped filter rule at Trace and resolves
        // the most specific matching rule, so its own Information default never reaches Serilog.
        // That is why Program.cs needs no SetMinimumLevel, and why adding one back would put a second
        // gate in front of the switch.
        WriteSettings("trace");
        using var host = BuildHost(Options());
        var store = host.Services.GetRequiredService<SettingsStore>();
        var logger = host.Services.GetRequiredService<ILogger<FileLoggingLevelTests>>();

        store.Load();
        host.Start();

        logger.IsEnabled(LogLevel.Trace).ShouldBeTrue();
        logger.LogTrace("Trace output.");

        _sink.WaitForMessageContaining("Trace output.").ShouldBeTrue();
    }

    [TestMethod]
    public void AnUnrecognisedLevel_FallsBackToInformationAndSaysWhatItFound()
    {
        WriteSettings("chatty");
        using var host = BuildHost(Options());
        var store = host.Services.GetRequiredService<SettingsStore>();

        store.Load();
        host.Start();

        _sink.WaitForMessageContaining("chatty").ShouldBeTrue();
        host.Services.GetRequiredService<LoggingLevelSwitch>()
            .MinimumLevel
            .ShouldBe(Serilog.Events.LogEventLevel.Information);
    }
}
