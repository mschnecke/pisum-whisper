namespace Pisum.Whisper.Core.Tests.Logging;

using Pisum.Whisper.Core.Logging;
using Serilog.Events;
using Shouldly;

[TestClass]
public sealed class LogLevelNamesTests
{
    [TestMethod]
    [DataRow("trace", LogEventLevel.Verbose)]
    [DataRow("debug", LogEventLevel.Debug)]
    [DataRow("info", LogEventLevel.Information)]
    [DataRow("warn", LogEventLevel.Warning)]
    [DataRow("error", LogEventLevel.Error)]
    public void TryParse_MapsTheFiveAcceptedNames(string name, LogEventLevel expected)
    {
        LogLevelNames.TryParse(name, out var level).ShouldBeTrue();
        level.ShouldBe(expected);
    }

    [TestMethod]
    [DataRow("DEBUG", LogEventLevel.Debug)]
    [DataRow("Warn", LogEventLevel.Warning)]
    [DataRow("eRRoR", LogEventLevel.Error)]
    public void TryParse_IgnoresCase(string name, LogEventLevel expected)
    {
        // The reference matches case-insensitively and the settings file is hand-editable. That is
        // parity, not added flexibility.
        LogLevelNames.TryParse(name, out var level).ShouldBeTrue();
        level.ShouldBe(expected);
    }

    [TestMethod]
    [DataRow("Verbose")]
    [DataRow("Information")]
    [DataRow("Warning")]
    [DataRow("fatal")]
    [DataRow("chatty")]
    [DataRow("")]
    [DataRow(null)]
    public void TryParse_RejectsAnythingElseAndFallsBackToInformation(string? name)
    {
        // Serilog spellings are deliberately not aliases: settings offers a dropdown, so free text
        // only reaches this from a hand-edit, and a hand-edit deserves to be told.
        LogLevelNames.TryParse(name, out var level).ShouldBeFalse();
        level.ShouldBe(LogEventLevel.Information);
    }
}
