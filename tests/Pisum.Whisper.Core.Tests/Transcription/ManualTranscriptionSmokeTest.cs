namespace Pisum.Whisper.Core.Tests.Transcription;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Task 8.1 — the only thing that exercises the real wire format against the real API, in the same
/// role <c>ManualCaptureSmokeTest</c> plays for the microphone. Every other test in this folder runs
/// against a scripted handler, so a change to Gemini's contract would otherwise go unnoticed.
/// </summary>
/// <remarks>
/// Set <c>PISUM_WHISPER_GEMINI_KEY</c> and run this by name. It sends a second of synthesised tone,
/// so it costs a token or two and asserts only that the round trip completes — what a model returns
/// for a tone is not something to assert on.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Manual)]
public sealed class ManualTranscriptionSmokeTest
{
    private const string KeyVariable = "PISUM_WHISPER_GEMINI_KEY";

    [Fact(
        Skip = "Requires a real Gemini API key; run manually",
        SkipUnless = nameof(ManualTests.Enabled),
        SkipType = typeof(ManualTests))]
    public async Task ARealRoundTrip_Completes()
    {
        var apiKey = Environment.GetEnvironmentVariable(KeyVariable);
        apiKey.ShouldNotBeNullOrWhiteSpace($"Set {KeyVariable} before running this test.");

        var home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(home);

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(provider => new SettingsStore(
                provider.GetRequiredService<ILogger<SettingsStore>>(),
                Path.Combine(home, ".pisum-whisper.json")));
            services.AddGeminiTranscription();

            using var container = services.BuildServiceProvider();

            var store = container.GetRequiredService<SettingsStore>();
            store.Load();
            store.Current.Providers.Add(new ProviderConfig {Id = "manual", ApiKey = apiKey!});

            // A second of 440 Hz at the capture pipeline's rate, encoded by the real encoder — so
            // this exercises change 4's output rather than a hand-built byte array.
            var samples = new float[48_000];
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = 0.25f * MathF.Sin(2 * MathF.PI * 440 * index / 48_000);
            }

            var encoded = new AudioEncoder(NullLogger<AudioEncoder>.Instance)
                .Encode(samples, 48_000, AudioFormat.Opus);

            var transcript = await container.GetRequiredService<ITranscriptionProvider>()
                .TranscribeAsync(encoded, "Transcribe the audio. Output only the transcription.",
                    CancellationToken.None);

            transcript.ShouldNotBeNull();
            Console.WriteLine($"Gemini returned {transcript.Length} characters.");
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }
}
