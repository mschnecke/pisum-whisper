namespace Pisum.Whisper.Core.Tests.Dictation;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Audio;
using Pisum.Whisper.Core.Dictation;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Task 5.1 — what shutting down does to each state.
/// </summary>
[TestClass]
public sealed class DictationLifecycleTests : DictationTestBase
{
    [TestMethod]
    public async Task ShuttingDownStopsObservingTheHotkey()
    {
        var orchestrator = Create();

        await orchestrator.StopAsync(CancellationToken.None);

        Hotkeys.Press();

        Capture.Starts.ShouldBe(0);
    }

    [TestMethod]
    public async Task ShuttingDownWhileRecordingDiscardsTheRecording()
    {
        var orchestrator = Create();

        Hotkeys.Press();
        Clock.Advance(TimeSpan.FromSeconds(5));

        await orchestrator.StopAsync(CancellationToken.None);

        Capture.Stops.ShouldBe(1);
        Provider.Calls.ShouldBe(0);
        Output.Calls.ShouldBe(0);
        orchestrator.State.ShouldBe(DictationState.Idle);
    }

    /// <summary>
    /// The transcription is abandoned rather than waited out — the whole point of cancelling it is
    /// that the budget could otherwise be two minutes.
    /// </summary>
    [TestMethod]
    public async Task ShuttingDownWhileTranscribingAbandonsItPromptly()
    {
        var orchestrator = Create(transcriptionBudget: TimeSpan.FromMinutes(5));
        Provider.Hang = true;

        Dictate(TimeSpan.FromSeconds(2));
        await Provider.Entered;

        var elapsed = Stopwatch.StartNew();
        await orchestrator.StopAsync(CancellationToken.None);
        elapsed.Stop();

        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        Output.Calls.ShouldBe(0);
    }

    /// <summary>
    /// The test that protects the user's clipboard. Between <c>TextOutput</c> writing the transcript
    /// and restoring what was there before, the previous contents exist nowhere but inside that
    /// call — and on Windows the transcript outlives this process, because <c>SetClipboardData</c>
    /// hands ownership to the system. A <c>StopAsync</c> that cancelled without awaiting would let
    /// the process exit inside that window and destroy the clipboard permanently.
    /// </summary>
    [TestMethod]
    public async Task ShuttingDownWhileDeliveringWaitsForTheDeliveryToFinish()
    {
        var orchestrator = Create();
        Output.Block();

        Dictate(TimeSpan.FromSeconds(2));
        await Output.Entered;

        var stopping = orchestrator.StopAsync(CancellationToken.None);

        await Task.Delay(100);
        stopping.IsCompleted.ShouldBeFalse("shutdown must not return while a delivery is in flight");

        Output.Release();
        await stopping;

        Output.Calls.ShouldBe(1);
    }

    [TestMethod]
    public async Task ShuttingDownWhenIdleDoesNothing()
    {
        var orchestrator = Create();

        await orchestrator.StopAsync(CancellationToken.None);

        Capture.Starts.ShouldBe(0);
        Capture.Stops.ShouldBe(0);
        orchestrator.State.ShouldBe(DictationState.Idle);
    }
}

/// <summary>
/// Task 5.2 — the registration itself, exercised rather than reconstructed.
/// </summary>
[TestClass]
public sealed class DictationRegistrationTests
{
    private string _home = string.Empty;

    [TestInitialize]
    public void CreateTemporaryHome()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);
    }

    [TestCleanup]
    public void RemoveTemporaryHome()
    {
        Directory.Delete(_home, true);
    }

    [TestMethod]
    public void TheRegistrationSatisfiesContainerValidation()
    {
        // The application builds its container with ValidateOnBuild, so an unsatisfiable dependency
        // here is a startup failure rather than a null reference at the first hotkey press.
        Should.NotThrow(() => BuildHost(validate: true).Dispose());
    }

    /// <summary>
    /// Both roles must be one instance. The orchestrator subscribes to the hotkey in its
    /// constructor, so a second instance would be a second subscriber, and one key press would open
    /// two recordings over one microphone.
    /// </summary>
    [TestMethod]
    public void BothRolesResolveToOneInstance()
    {
        using var host = BuildHost();

        var concrete = host.Services.GetRequiredService<DictationOrchestrator>();
        var hosted = host.Services.GetServices<IHostedService>().OfType<DictationOrchestrator>().Single();

        hosted.ShouldBeSameAs(concrete);
    }

    [TestMethod]
    public void TheOrchestratorStartsIdle()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<DictationOrchestrator>().State.ShouldBe(DictationState.Idle);
    }

    private IHost BuildHost(bool validate = false)
    {
        var builder = Host.CreateApplicationBuilder();

        if (validate)
        {
            builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            }));
        }

        builder.Services.AddSingleton(provider => new SettingsStore(
            provider.GetRequiredService<ILogger<SettingsStore>>(),
            Path.Combine(_home, ".pisum-whisper.json")));

        // Fakes for the five capabilities this one consumes, so the test is about this registration
        // rather than about theirs — and so that nothing here opens a microphone or a hook.
        builder.Services.AddSingleton<IGlobalHotkeyService, FakeHotkeyService>();
        builder.Services.AddSingleton<IAudioCapture, FakeAudioCapture>();
        builder.Services.AddSingleton<IAudioEncoder, FakeAudioEncoder>();
        builder.Services.AddSingleton<ITranscriptionProvider, FakeTranscriptionProvider>();
        builder.Services.AddSingleton<ITextOutput, FakeTextOutput>();

        builder.Services.AddDictationPipeline();

        return builder.Build();
    }
}
