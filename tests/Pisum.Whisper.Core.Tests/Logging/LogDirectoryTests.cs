namespace Pisum.Whisper.Core.Tests.Logging;

using Pisum.Whisper.Core.Logging;
using Shouldly;

[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class LogDirectoryTests : IDisposable
{
    private readonly string _home = string.Empty;

    public LogDirectoryTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        Directory.Delete(_home, true);
    }

    [Fact]
    public void DefaultPath_IsTheLogsFolderUnderTheApplicationDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        LogDirectory.DefaultPath().ShouldBe(Path.Combine(profile, ".pisum-whisper", "logs"));
        new LogDirectory().Path.ShouldBe(LogDirectory.DefaultPath());
    }

    [Fact]
    public void Path_IsResolvedWhetherOrNotTheDirectoryExists()
    {
        var directory = new LogDirectory(Path.Combine(_home, "logs"));

        Path.IsPathRooted(directory.Path).ShouldBeTrue();
        Directory.Exists(directory.Path).ShouldBeFalse();
        directory.LogFilePath.ShouldBe(Path.Combine(directory.Path, "pisum-whisper.log"));
    }

    [Fact]
    public void TryCreate_CreatesTheDirectoryAndReportsNoFailure()
    {
        var directory = new LogDirectory(Path.Combine(_home, "logs"));

        directory.TryCreate().ShouldBeNull();

        Directory.Exists(directory.Path).ShouldBeTrue();
    }

    [Fact]
    public void TryCreate_OverAnExistingDirectory_Succeeds()
    {
        var directory = new LogDirectory(Path.Combine(_home, "logs"));
        Directory.CreateDirectory(directory.Path);

        directory.TryCreate().ShouldBeNull();
    }

    [Fact]
    public void TryCreate_WhenTheDirectoryCannotBeCreated_ReturnsTheReasonRatherThanThrowing()
    {
        var path = Path.Combine(_home, "logs");
        File.WriteAllText(path, "not a directory");

        new LogDirectory(path).TryCreate().ShouldNotBeNullOrWhiteSpace();
    }

    // ---- Task 4.1: the reason survives the moment it was discovered ----

    [Fact]
    public void FailureReason_IsNullBeforeAnythingHasBeenTried()
    {
        new LogDirectory(Path.Combine(_home, "logs")).FailureReason.ShouldBeNull();
    }

    [Fact]
    public void FailureReason_IsNullAfterASuccessfulCreate()
    {
        var directory = new LogDirectory(Path.Combine(_home, "logs"));

        directory.TryCreate();

        directory.FailureReason.ShouldBeNull();
    }

    /// <summary>
    /// The reason is kept rather than discarded, because the component that can explain why there is
    /// no log is the one component whose explanation cannot be written to it. It is read much later,
    /// by <c>App.ReportStartupConditions</c>, off the very instance <c>AddFileLogging</c> registers.
    /// </summary>
    [Fact]
    public void FailureReason_SurvivesAFailedCreate()
    {
        // A file where the directory should be: unusable in the same way on both platforms.
        var path = Path.Combine(_home, "logs");
        File.WriteAllText(path, "not a directory");
        var directory = new LogDirectory(path);

        var returned = directory.TryCreate();

        directory.FailureReason.ShouldNotBeNullOrWhiteSpace();
        directory.FailureReason.ShouldBe(returned, "the retained reason is the one that was returned");
    }
}
