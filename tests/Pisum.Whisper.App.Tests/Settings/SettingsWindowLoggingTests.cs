namespace Pisum.Whisper.App.Tests.Settings;

using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.App.Tests;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Shell;
using Pisum.Whisper.Core.Tests.Logging;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Task 5.1 — this capability's logging rules, across every file the change adds.
/// </summary>
/// <remarks>
/// Never an API key value, never a preset's system prompt, and never a keystroke that is not the
/// accepted binding. The recorder is the sharpest edge: it sits on the one code path that observes
/// every key on the machine, so a line dumping what it saw would turn the log into a keylog that this
/// window's own Open Log Folder button then puts one click away. The sink runs at <c>Verbose</c>
/// behind the application's own <c>AddFileLogging</c>, so nothing a level gate would hide can hide
/// from these assertions either. Shaped after change 6's task 3.10 and change 8's task 5.3.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class SettingsWindowLoggingTests : SettingsEditorTestBase
{
    private const string ApiKey = "AIza-not-a-real-key-0123456789";

    private const string Prompt = "Rewrite my dictation as a formal letter of resignation.";

    /// <summary>Keys the recorder sees and rejects, which must reach the log no more than the accepted one does.</summary>
    private static readonly string[] RefusedKeys = ["Numpad7", "Backslash", "PrintScreen"];

    private readonly string _logHome;

    private readonly RecordingSink _sink = new();

    private readonly IHost _host;

    private readonly IGlobalHotkeyService _hotkeys = A.Fake<IGlobalHotkeyService>();

    private readonly IGeminiKeyProbe _probe = A.Fake<IGeminiKeyProbe>();

    public SettingsWindowLoggingTests()
    {
        _logHome = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_logHome);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddFileLogging(
            new FileLoggingOptions
            {
                Config = new LoggingConfig {LogLevel = "trace"},
                Directory = new LogDirectory(Path.Combine(_logHome, "logs")),
                SinkOverride = write => write.Sink(_sink),
            },
            out _);

        _host = builder.Build();
        A.CallTo(() => _hotkeys.Availability).Returns(HotkeyAvailability.Available);
    }

    public override void Dispose()
    {
        _host.Dispose();
        Directory.Delete(_logHome, true);
        base.Dispose();
    }

    [Fact]
    public async Task NoKeyValue_NoPromptText_AndNoKeyButTheAcceptedBindingIsEverWrittenDown()
    {
        var loggers = _host.Services.GetRequiredService<ILoggerFactory>();
        var editor = NewEditor(loggers.CreateLogger<SettingsEditor>());

        // A provider edit: the key box, the enable toggle and a model choice.
        var providers = new ProvidersViewModel(
            editor, _probe, new ModelListCache(loggers.CreateLogger<ModelListCache>()), Store.Current);
        providers.AddCommand.Execute(null);
        providers.Entries[0].ApiKey = ApiKey;
        providers.Entries[0].Enabled = false;
        await editor.FlushAsync();

        // A preset edit: the prompt is the user's own writing and is not a loggable value.
        var presets = new PresetsViewModel(
            Store, editor, new RecordingNotificationService(), loggers.CreateLogger<PresetsViewModel>());
        presets.NewName = "Resignation";
        presets.NewSystemPrompt = Prompt;
        await presets.AddCommand.ExecuteAsync(null);

        presets.Selected = presets.Presets.Single(preset => preset.Name == "Resignation");
        await presets.ActivateCommand.ExecuteAsync(null);

        // A capture: three keys the recorder refuses, then the one it accepts.
        var captures = new Queue<HotkeyCapture>(
        [
            HotkeyCapture.KeyNotSupported,
            Bare(RefusedKeys[0]),
            Bare(RefusedKeys[1]),
            Bare(RefusedKeys[2]),
            new HotkeyCapture(
                HotkeyCaptureOutcome.Captured,
                new HotkeyBinding {Modifiers = ["Alt", "Shift"], Key = "F9"}),
        ]);

        A.CallTo(() => _hotkeys.CaptureAsync(A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult(captures.Dequeue()));

        var hotkey = new HotkeyViewModel(
            editor, _hotkeys, loggers.CreateLogger<HotkeyViewModel>(), Store.Current);
        await hotkey.StartRecordingCommand.ExecuteAsync(null);
        await editor.FlushAsync();

        // A logging edit, which is the one that opens the folder the rest of this is written into.
        var logging = new LoggingViewModel(
            editor,
            new LogDirectory(Path.Combine(_logHome, "logs")),
            A.Fake<ISystemShell>(),
            loggers.CreateLogger<LoggingViewModel>(),
            Store.Current);
        logging.OpenLogFolderCommand.Execute(null);
        await editor.FlushAsync();

        // The sink sits behind the application's asynchronous wrapper, so it is drained on a
        // background worker. Waiting for the *last* thing written is what makes the scan below run
        // against a settled sink rather than against a race.
        _sink.WaitForMessageContaining("Alt + Shift + F9").ShouldBeTrue();

        foreach (var logEvent in _sink.Events)
        {
            var text = logEvent.RenderMessage();
            var written = string.Join(
                "\n", text, string.Join("\n", logEvent.Properties.Select(pair => pair.Value.ToString())));

            written.ShouldNotContain(ApiKey);
            written.ShouldNotContain(Prompt);

            foreach (var refused in RefusedKeys)
            {
                written.ShouldNotContain(refused);
            }
        }

        // The accepted chord is loggable, and asserting it is here is what proves the assertions
        // above are not passing because nothing was logged at all.
        _sink.Messages.ShouldContain(message => message.Contains("Alt + Shift + F9"));
    }

    private static HotkeyCapture Bare(string key)
    {
        return new HotkeyCapture(
            HotkeyCaptureOutcome.Captured, new HotkeyBinding {Modifiers = [], Key = key});
    }
}
