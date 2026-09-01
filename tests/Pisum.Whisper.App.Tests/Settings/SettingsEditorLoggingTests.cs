namespace Pisum.Whisper.App.Tests.Settings;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Tests.Logging;
using Shouldly;

/// <summary>
/// Task 2.6 — that a commit is logged as a count of changed settings and nothing more.
/// </summary>
/// <remarks>
/// This type holds the object that carries the user's API keys and preset prompts, and the settings
/// window puts the log file one click away through its own Open Log Folder button. The sink is wired
/// behind the application's own <c>AddFileLogging</c> at <c>Verbose</c>, so nothing a level gate
/// would have hidden can hide from the assertion either.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class SettingsEditorLoggingTests : SettingsEditorTestBase
{
    private const string ApiKey = "AIza-not-a-real-key-0123456789";

    private const string Prompt = "Rewrite my dictation as a formal letter of resignation.";

    private readonly string _logHome;

    private readonly RecordingSink _sink = new();

    private readonly IHost _host;

    public SettingsEditorLoggingTests()
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
    }

    [Fact]
    public async Task ACommitWritesACountAndNeitherAKeyNorAPrompt()
    {
        var editor = NewEditor(_host.Services.GetRequiredService<ILogger<SettingsEditor>>());

        editor.Edit(settings => settings.Providers.Add(
            new ProviderConfig {Id = "one", ApiKey = ApiKey, Model = "gemini-2.5-flash"}));
        editor.Edit(settings => settings.Presets[0].SystemPrompt = Prompt);
        await editor.FlushAsync();

        _sink.WaitForMessageContaining("Committing").ShouldBeTrue();

        var events = _sink.Events;
        events.ShouldNotBeEmpty();

        foreach (var logEvent in events)
        {
            logEvent.RenderMessage().ShouldNotContain(ApiKey);
            logEvent.RenderMessage().ShouldNotContain(Prompt);

            foreach (var property in logEvent.Properties)
            {
                property.Value.ToString().ShouldNotContain(ApiKey);
                property.Value.ToString().ShouldNotContain(Prompt);
            }
        }

        // The count is what it does write, so the assertion above is not passing because nothing
        // was logged at all.
        _sink.Messages.ShouldContain(message => message.Contains("Committing 2 settings changes"));
    }

    public override void Dispose()
    {
        _host.Dispose();
        Directory.Delete(_logHome, true);
        base.Dispose();
    }
}
