namespace Pisum.Whisper.Core.Tests.Logging;

using Pisum.Whisper.Core.Logging;
using Serilog.Events;
using Shouldly;

public sealed class LogLevelNamesTests
{
    [Theory]
    [InlineData("trace", LogEventLevel.Verbose)]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData("info", LogEventLevel.Information)]
    [InlineData("warn", LogEventLevel.Warning)]
    [InlineData("error", LogEventLevel.Error)]
    public void TryParse_MapsTheFiveAcceptedNames(string name, LogEventLevel expected)
    {
        LogLevelNames.TryParse(name, out var level).ShouldBeTrue();
        level.ShouldBe(expected);
    }

    [Theory]
    [InlineData("DEBUG", LogEventLevel.Debug)]
    [InlineData("Warn", LogEventLevel.Warning)]
    [InlineData("eRRoR", LogEventLevel.Error)]
    public void TryParse_IgnoresCase(string name, LogEventLevel expected)
    {
        // The reference matches case-insensitively and the settings file is hand-editable. That is
        // parity, not added flexibility.
        LogLevelNames.TryParse(name, out var level).ShouldBeTrue();
        level.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("fatal")]
    [InlineData("chatty")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsAnythingElseAndFallsBackToInformation(string? name)
    {
        // Serilog spellings are deliberately not aliases: settings offers a dropdown, so free text
        // only reaches this from a hand-edit, and a hand-edit deserves to be told.
        LogLevelNames.TryParse(name, out var level).ShouldBeFalse();
        level.ShouldBe(LogEventLevel.Information);
    }
}
