namespace Pisum.Whisper.Platform.Tests.Dictation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Pisum.Whisper.Platform.Output;
using Shouldly;
using Pisum.Whisper.Platform.Tests;

/// <summary>
/// The whole product in one test: a real microphone, a real encode, a real Gemini round trip and a
/// real clipboard and paste. It is the repeatable half of task 6.1 — the other half is launching the
/// tray application and pressing the key by hand, which nothing automated can do.
/// </summary>
/// <remarks>
/// <para>
/// It lives here rather than in <c>Core.Tests</c> because the real <see cref="ITextOutput"/> needs
/// <see cref="ISystemClipboard"/> and <see cref="IPasteProbe"/>, which only
/// <c>Pisum.Whisper.Platform</c> supplies — and change 7 deliberately kept <c>Core.Tests</c> free of
/// a reference to the platform layer.
/// </para>
/// <para>
/// <b>Before running it:</b> put the caret in a text editor and leave that window focused, because
/// the transcript is pasted wherever focus is. It reads your real
/// <c>~/.pisum-whisper.json</c>, so the API key it uses is the one already configured. Speak for the
/// five seconds after it starts.
/// </para>
/// </remarks>
public sealed class ManualDictationSmokeTest
{
    [Fact(
        Skip = "Requires a microphone, a configured API key and a desktop session; run manually",
        SkipUnless = nameof(ManualTests.Enabled),
        SkipType = typeof(ManualTests))]
    public async Task SpeakingForFiveSecondsPutsTheWordsAtTheCursor()
    {
        using var host = BuildHost();
        var hotkeys = (StubHotkeyService)host.Services.GetRequiredService<IGlobalHotkeyService>();
        var orchestrator = host.Services.GetRequiredService<DictationOrchestrator>();

        await host.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            hotkeys.Press();
            orchestrator.State.ShouldBe(DictationState.Recording);

            // Speak now.
            await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            hotkeys.Release();

            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (orchestrator.State != DictationState.Idle && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            orchestrator.State.ShouldBe(
                DictationState.Idle,
                "the dictation did not finish inside the transcription budget");
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }

        // What actually happened is in the log and at the cursor: check that the words you spoke are
        // in the focused editor, and that the clipboard still holds whatever it held before.
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddAudioPipeline();
        builder.Services.AddGeminiTranscription();
        builder.Services.AddTextOutput();
        builder.Services.AddNativeOutput();
        builder.Services.AddDictationPipeline();

        // The one substitution. Everything else is the real thing, wired the way Program.cs wires
        // it; a real global hook would need a human to press the key, which is task 6.1's job.
        builder.Services.AddSingleton<IGlobalHotkeyService, StubHotkeyService>();

        var host = builder.Build();
        host.Services.GetRequiredService<SettingsStore>().Load();

        return host;
    }

    /// <summary>Stands in for the hook so the test can raise both edges itself.</summary>
    private sealed class StubHotkeyService : IGlobalHotkeyService
    {
        public event EventHandler? Pressed;

        public event EventHandler? Released;

        public HotkeyAvailability Availability => HotkeyAvailability.Available;

        public HotkeyChord Chord => HotkeyChord.Default;

        public Task<HotkeyCapture> CaptureAsync(CancellationToken cancellationToken) =>
            Task.FromResult(HotkeyCapture.Cancelled);

        public void Press() => Pressed?.Invoke(this, EventArgs.Empty);

        public void Release() => Released?.Invoke(this, EventArgs.Empty);
    }
}
