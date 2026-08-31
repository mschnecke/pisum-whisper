namespace Pisum.Whisper.Core.Tests.Transcription;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>
/// Task 7.1 — the registration itself, exercised rather than reconstructed.
/// </summary>
[IntegrationTest]
public sealed class TranscriptionRegistrationTests : IDisposable
{
    private readonly string _home = string.Empty;

    public TranscriptionRegistrationTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        Directory.Delete(_home, true);
    }

    [Fact]
    public void TheRegistrationSatisfiesContainerValidation()
    {
        // The application builds its container with ValidateOnBuild, so an unsatisfiable dependency
        // here is a startup failure rather than a null reference at first use.
        Should.NotThrow(() => BuildHost(true).Dispose());
    }

    [Fact]
    public void TheProviderResolvesAsThePool()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<ITranscriptionProvider>().ShouldBeOfType<GeminiProviderPool>();
    }

    [Fact]
    public void TheKeyProbeResolves()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<IGeminiKeyProbe>().ShouldBeOfType<GeminiKeyProbe>();
    }

    [Fact]
    public void TheNamedClientCarriesTheBaseAddressAndTimeout()
    {
        using var host = BuildHost();

        var client = host.Services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(GeminiHttpClient.Name);

        client.BaseAddress.ShouldBe(GeminiHttpClient.BaseAddress);
        client.Timeout.ShouldBe(GeminiHttpClient.Timeout);
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

        builder.Services.AddGeminiTranscription();

        return builder.Build();
    }
}
