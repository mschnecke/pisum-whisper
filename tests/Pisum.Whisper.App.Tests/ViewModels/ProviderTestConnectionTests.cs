namespace Pisum.Whisper.App.Tests.ViewModels;

using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.App.Tests.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>Task 4.6 — Test Connection, rendered inline from <see cref="KeyProbeResult"/>.</summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class ProviderTestConnectionTests : SettingsEditorTestBase
{
    private readonly IGeminiKeyProbe _probe = A.Fake<IGeminiKeyProbe>();

    private ProviderEntryViewModel NewEntry(string apiKey = "AIza-one")
    {
        return new ProviderEntryViewModel(
            NewEditor(),
            _probe,
            new ModelListCache(NullLogger<ModelListCache>.Instance),
            "one",
            apiKey,
            null,
            true);
    }

    private void Answer(KeyProbeResult result)
    {
        A.CallTo(() => _probe.TestConnectionAsync(A<string>._, A<string?>._, A<CancellationToken>._))
            .Returns(result);
    }

    [Fact]
    public async Task ASuccessIsReported()
    {
        Answer(new KeyProbeResult(true, "Connection succeeded.", null));
        var entry = NewEntry();

        await entry.TestConnectionCommand.ExecuteAsync(null);

        entry.TestSucceeded.ShouldBeTrue();
        entry.TestResult.ShouldBe("Connection succeeded.");
    }

    [Fact]
    public async Task ARejectedKeyIsReportedWithItsCategory()
    {
        Answer(new KeyProbeResult(false, "Gemini returned 401: bad key", ErrorCategory.Authentication));
        var entry = NewEntry();

        await entry.TestConnectionCommand.ExecuteAsync(null);

        entry.TestSucceeded.ShouldBeFalse();
        entry.TestResult.ShouldNotBeNull().ShouldContain("Gemini returned 401");
        entry.TestResult.ShouldNotBeNull().ShouldContain(nameof(ErrorCategory.Authentication));
    }

    [Fact]
    public async Task AnUnreachableServiceIsReportedAsSuchRatherThanAsARejectedKey()
    {
        Answer(new KeyProbeResult(false, "Gemini could not be reached.", ErrorCategory.Network));
        var entry = NewEntry();

        await entry.TestConnectionCommand.ExecuteAsync(null);

        entry.TestSucceeded.ShouldBeFalse();
        entry.TestResult.ShouldNotBeNull().ShouldContain("could not be reached");
        entry.TestResult.ShouldNotBeNull().ShouldContain(nameof(ErrorCategory.Network));
    }

    [Fact]
    public async Task TheButtonIsDisabledWhileATestIsInFlight()
    {
        var release = new TaskCompletionSource<KeyProbeResult>();
        A.CallTo(() => _probe.TestConnectionAsync(A<string>._, A<string?>._, A<CancellationToken>._))
            .Returns(release.Task);

        var entry = NewEntry();
        var running = entry.TestConnectionCommand.ExecuteAsync(null);

        entry.IsBusy.ShouldBeTrue();
        entry.TestConnectionCommand.CanExecute(null).ShouldBeFalse();
        entry.TestResult.ShouldBe("Testing...");

        release.SetResult(new KeyProbeResult(true, "Connection succeeded.", null));
        await running;

        entry.IsBusy.ShouldBeFalse();
        entry.TestConnectionCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void TheButtonIsUnavailableWhileTheKeyIsEmpty()
    {
        NewEntry(string.Empty).TestConnectionCommand.CanExecute(null).ShouldBeFalse();
    }
}
