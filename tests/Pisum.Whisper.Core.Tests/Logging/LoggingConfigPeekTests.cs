namespace Pisum.Whisper.Core.Tests.Logging;

using Pisum.Whisper.Core.Logging;
using Shouldly;

public sealed class LoggingConfigPeekTests : IDisposable
{
    private string _directory = string.Empty;

    private string _path = string.Empty;

    public LoggingConfigPeekTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, ".pisum-whisper.json");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    /// <summary>
    /// The peek runs before anything owns the settings file, so leaving it exactly as it was found —
    /// or absent — is the whole of its contract with the settings store.
    /// </summary>
    private void ShouldHaveLeftTheFileAlone(byte[]? before)
    {
        if (before is null)
        {
            File.Exists(_path).ShouldBeFalse();
            Directory.GetFiles(_directory).ShouldBeEmpty();
        }
        else
        {
            File.ReadAllBytes(_path).ShouldBe(before);
            Directory.GetFiles(_directory).ShouldBe([_path]);
        }
    }

    [Fact]
    public void Read_WithNoFile_ReturnsDefaultsAndCreatesNothing()
    {
        var config = LoggingConfigPeek.Read(_path);

        config.LogLevel.ShouldBe("info");
        config.LogMaxFileSizeMb.ShouldBe(1);
        config.LogRetentionDays.ShouldBe(7);
        ShouldHaveLeftTheFileAlone(null);
    }

    [Fact]
    public void Read_WithAValidFile_ReturnsTheConfiguredValues()
    {
        File.WriteAllText(
            _path,
            """{"loggingConfig": {"logLevel": "debug", "logMaxFileSizeMb": 5, "logRetentionDays": 2}}""");
        var before = File.ReadAllBytes(_path);

        var config = LoggingConfigPeek.Read(_path);

        config.LogLevel.ShouldBe("debug");
        config.LogMaxFileSizeMb.ShouldBe(5);
        config.LogRetentionDays.ShouldBe(2);
        ShouldHaveLeftTheFileAlone(before);
    }

    [Fact]
    public void Read_WithAPartialFile_DefaultsTheRest()
    {
        File.WriteAllText(_path, """{"startWithSystem": false, "loggingConfig": {"logLevel": "warn"}}""");
        var before = File.ReadAllBytes(_path);

        var config = LoggingConfigPeek.Read(_path);

        config.LogLevel.ShouldBe("warn");
        config.LogMaxFileSizeMb.ShouldBe(1);
        config.LogRetentionDays.ShouldBe(7);
        ShouldHaveLeftTheFileAlone(before);
    }

    [Fact]
    public void Read_WithNoLoggingSection_ReturnsDefaults()
    {
        File.WriteAllText(_path, """{"startWithSystem": false}""");
        var before = File.ReadAllBytes(_path);

        LoggingConfigPeek.Read(_path).LogLevel.ShouldBe("info");
        ShouldHaveLeftTheFileAlone(before);
    }

    [Fact]
    public void Read_WithACorruptFile_ReturnsDefaultsRatherThanThrowing()
    {
        // The settings store throws over this file a moment later, with a logger that by then
        // exists. The peek cannot: it is what the logger is built from.
        File.WriteAllText(_path, """{"loggingConfig": {"logLevel": tru""");
        var before = File.ReadAllBytes(_path);

        LoggingConfigPeek.Read(_path).LogLevel.ShouldBe("info");
        ShouldHaveLeftTheFileAlone(before);
    }

    [Fact]
    public void Read_WithAnUnreadableFile_ReturnsDefaultsRatherThanThrowing()
    {
        // A directory where the file should be: unreadable in the same way on every platform.
        Directory.CreateDirectory(_path);

        LoggingConfigPeek.Read(_path).LogLevel.ShouldBe("info");

        Directory.Delete(_path);
    }
}
