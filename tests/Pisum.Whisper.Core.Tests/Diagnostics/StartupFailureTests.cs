namespace Pisum.Whisper.Core.Tests.Diagnostics;

using System.Text.Json;
using Pisum.Whisper.Core.Diagnostics;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>
/// Task 1.2 — the startup failure vocabulary. The title comes from what failed, never from matching
/// the text of a message, which is <c>DictationFailure</c>'s rule applied one layer out.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class StartupFailureTests
{
    private const string LogPath = @"C:\Users\someone\.pisum-whisper\logs\pisum-whisper.log";

    [Fact]
    public void AnUnreadableSettingsFileIsASettingsError()
    {
        var (title, message) = StartupFailure.Describe(
            new SettingsException(@"The settings file 'C:\home\.pisum-whisper.json' could not be parsed: bad."),
            LogPath);

        title.ShouldBe("Settings Error");
        message.ShouldContain("could not be parsed");
        message.ShouldContain(".pisum-whisper.json");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnwritableSettingsFileIsASettingsError(bool denied)
    {
        // SettingsStore.Write now wraps its own failure exactly as Read() already wraps its, so this
        // arrives as a SettingsException whether it happened on a first launch or a later Save.
        Exception inner = denied
            ? new UnauthorizedAccessException(@"Access to the path 'C:\home\.pisum-whisper.json.tmp' is denied.")
            : new IOException(@"There is not enough space on the disk.");
        var thrown = new SettingsException(
            $@"The settings file 'C:\home\.pisum-whisper.json' could not be written: {inner.Message}", inner);

        var (title, message) = StartupFailure.Describe(thrown, LogPath);

        title.ShouldBe("Settings Error");
        message.ShouldContain("could not be written");
        message.ShouldContain(inner.Message);
    }

    [Fact]
    public void ANonSettingsIOExceptionIsAStartupError()
    {
        // Issue #34's exact reproduction: Avalonia.Platform.StandardAssetLoader.Open raises this, an
        // IOException subclass, for a missing tray icon resource — nothing to do with settings.
        var (title, message) = StartupFailure.Describe(
            new FileNotFoundException(
                "The resource avares://Pisum.Whisper.App/Assets/tray-idle.png could not be found."),
            LogPath);

        title.ShouldBe("Startup Error");
        message.ShouldContain(StartupFailure.StartupErrorMessage);
        message.ShouldNotContain("settings", Case.Insensitive);
        message.ShouldNotContain(".pisum-whisper.json");
    }

    [Fact]
    public void AnythingElseIsAStartupError()
    {
        // What ValidateOnBuild and a missing tray asset both arrive as.
        var (title, message) = StartupFailure.Describe(
            new InvalidOperationException("Unable to resolve service for type 'ISystemClipboard'."),
            LogPath);

        title.ShouldBe("Startup Error");
        message.ShouldContain("Pisum Whisper could not start.");

        // The dialog is a pointer to the detail rather than the detail: the resolution failure's own
        // message is a developer's, and it is in the log.
        message.ShouldNotContain("ISystemClipboard");
    }

    public static TheoryData<Exception> EveryDescribedKind =>
    [
        new SettingsException("something went wrong"),
        new UnauthorizedAccessException("something went wrong"),
        new IOException("something went wrong"),
        new InvalidOperationException("something went wrong"),
    ];

    /// <summary>The dialog is a pointer to the detail, so every arm of the table has to point.</summary>
    [Theory]
    [MemberData(nameof(EveryDescribedKind))]
    public void EveryMessageSaysWhereTheLogWouldBe(Exception exception)
    {
        var (_, message) = StartupFailure.Describe(exception, LogPath);

        message.ShouldContain(LogPath);

        // "would be", not "is": an unusable log directory and a fatal failure can coincide, and then
        // the file this names does not exist.
        message.ShouldContain("would be written to");
    }

    [Fact]
    public void WithNoLogPathTheMessageSaysNothingAboutOne()
    {
        var (_, message) = StartupFailure.Describe(new InvalidOperationException("no"), null);

        message.ShouldBe("Pisum Whisper could not start.");
    }

    /// <summary>
    /// Task 1.1's decision, guarded rather than restated. The parse message is passed through
    /// unchanged because summarising it deletes what makes the file repairable by hand — so the
    /// claim that it cannot carry a key value is asserted against a genuine parse failure over a
    /// document that holds one, in the manner of <c>DictationNotificationTests</c>.
    /// </summary>
    [Fact]
    public void AParseFailureInsideAnApiKeyDisclosesNoPartOfIt()
    {
        const string key = "AIzaSyExampleKeyMaterialThatMustNotLeak";

        // A missing comma after the key's value: the reader stops on the quote that opens the next
        // property name, which is the corruption rather than the content.
        var corrupt = $$"""{"providers":[{"id":"a","apiKey":"{{key}}" "enabled":true}]}""";

        var exception = Should.Throw<SettingsException>(() =>
        {
            try
            {
                JsonSerializer.Deserialize(corrupt, SettingsJsonContext.OnDisk.AppSettings);
            }
            catch (JsonException parse)
            {
                // Exactly SettingsStore.Read's wrapping, without touching a file.
                throw new SettingsException(
                    $@"The settings file 'C:\home\.pisum-whisper.json' could not be parsed: {parse.Message}",
                    parse);
            }
        });

        var (title, message) = StartupFailure.Describe(exception, LogPath);

        title.ShouldNotContain(key);
        message.ShouldNotContain(key);

        // Not merely the whole value: no run of it long enough to be worth having either.
        message.ShouldNotContain(key[..8]);
        message.ShouldNotContain(key[^8..]);

        // The property name is deliberately permitted — it is what makes the file repairable, and
        // the schema is in the repository already.
        message.ShouldContain("could not be parsed");
    }
}
