using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pisum.Whisper.Core.Logging;
using Shouldly;

namespace Pisum.Whisper.Core.Tests.Logging;

[TestClass]
public sealed class LogDirectoryTests
{
    private string _home = string.Empty;

    [TestInitialize]
    public void CreateTemporaryHome()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);
    }

    [TestCleanup]
    public void RemoveTemporaryHome() => Directory.Delete(_home, recursive: true);

    [TestMethod]
    public void DefaultPath_IsTheLogsFolderUnderTheApplicationDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        LogDirectory.DefaultPath().ShouldBe(Path.Combine(profile, ".pisum-whisper", "logs"));
        new LogDirectory().Path.ShouldBe(LogDirectory.DefaultPath());
    }

    [TestMethod]
    public void Path_IsResolvedWhetherOrNotTheDirectoryExists()
    {
        var directory = new LogDirectory(Path.Combine(_home, "logs"));

        Path.IsPathRooted(directory.Path).ShouldBeTrue();
        Directory.Exists(directory.Path).ShouldBeFalse();
        directory.LogFilePath.ShouldBe(Path.Combine(directory.Path, "pisum-whisper.log"));
    }

    [TestMethod]
    public void TryCreate_CreatesTheDirectoryAndReportsNoFailure()
    {
        var directory = new LogDirectory(Path.Combine(_home, "logs"));

        directory.TryCreate().ShouldBeNull();

        Directory.Exists(directory.Path).ShouldBeTrue();
    }

    [TestMethod]
    public void TryCreate_OverAnExistingDirectory_Succeeds()
    {
        var directory = new LogDirectory(Path.Combine(_home, "logs"));
        Directory.CreateDirectory(directory.Path);

        directory.TryCreate().ShouldBeNull();
    }

    [TestMethod]
    public void TryCreate_WhenTheDirectoryCannotBeCreated_ReturnsTheReasonRatherThanThrowing()
    {
        var path = Path.Combine(_home, "logs");
        File.WriteAllText(path, "not a directory");

        new LogDirectory(path).TryCreate().ShouldNotBeNullOrWhiteSpace();
    }
}
