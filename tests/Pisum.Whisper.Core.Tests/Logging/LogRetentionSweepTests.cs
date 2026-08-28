using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pisum.Whisper.Core.Logging;
using Shouldly;

namespace Pisum.Whisper.Core.Tests.Logging;

[TestClass]
public sealed class LogRetentionSweepTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void CreateTemporaryDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void RemoveTemporaryDirectory() => Directory.Delete(_directory, recursive: true);

    private void WriteAged(string name, double ageInDays)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromDays(ageInDays));
    }

    private string[] RemainingFiles() =>
        [.. Directory.GetFiles(_directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    [TestMethod]
    public void Run_DeletesOnlyTheFilesPastTheRetentionBoundary()
    {
        // Serilog rolls to pisum-whisper_001.log, so the sequence precedes the extension and the
        // base-named file is the oldest rather than the newest.
        WriteAged("pisum-whisper.log", 30);
        WriteAged("pisum-whisper_001.log", 7.5);
        WriteAged("pisum-whisper_002.log", 6.5);
        WriteAged("pisum-whisper_003.log", 0);

        var removed = LogRetentionSweep.Run(_directory, retentionDays: 7);

        removed.Order(StringComparer.Ordinal).ShouldBe(["pisum-whisper.log", "pisum-whisper_001.log"]);
        RemainingFiles().ShouldBe(["pisum-whisper_002.log", "pisum-whisper_003.log"]);
    }

    [TestMethod]
    public void Run_LeavesFilesItDoesNotOwnAlone()
    {
        WriteAged("pisum-whisper.log", 30);
        WriteAged("notes.txt", 30);
        WriteAged("other-app.log", 30);

        LogRetentionSweep.Run(_directory, retentionDays: 7).ShouldBe(["pisum-whisper.log"]);

        RemainingFiles().ShouldBe(["notes.txt", "other-app.log"]);
    }

    [TestMethod]
    public void Run_WithNothingExpired_RemovesNothing()
    {
        WriteAged("pisum-whisper.log", 1);

        LogRetentionSweep.Run(_directory, retentionDays: 7).ShouldBeEmpty();

        RemainingFiles().ShouldBe(["pisum-whisper.log"]);
    }

    [TestMethod]
    public void Run_OverAMissingDirectory_ReturnsEmptyRatherThanThrowing()
    {
        // Housekeeping runs before the directory is known to be usable, and must not be the thing
        // that stops the application starting.
        LogRetentionSweep.Run(Path.Combine(_directory, "absent"), retentionDays: 7).ShouldBeEmpty();
    }
}
