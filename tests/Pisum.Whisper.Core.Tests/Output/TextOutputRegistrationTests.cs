namespace Pisum.Whisper.Core.Tests.Output;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisum.Whisper.Core.Output;
using SharpHook.Simulation;
using Shouldly;

/// <summary>
/// Task 4.1 — the registration itself, exercised rather than reconstructed, in the shape of
/// <see cref="Transcription.TranscriptionRegistrationTests"/>.
/// </summary>
/// <remarks>
/// The native half lives in <c>Pisum.Whisper.Platform</c>, which this project deliberately does not
/// reference — the tests for the layer with no platform dependencies do not acquire one. The fakes
/// stand in for it, and that <c>AddNativeOutput</c> registers what is missing here is asserted in
/// <c>Pisum.Whisper.Platform.Tests</c>.
/// </remarks>
[IntegrationTest]
public sealed class TextOutputRegistrationTests
{
    [Fact]
    public void TheRegistrationSatisfiesContainerValidation()
    {
        Should.NotThrow(() => BuildHost(withNativeHalf: true).Dispose());
    }

    [Fact]
    public void TheDeliveryResolves()
    {
        using var host = BuildHost(withNativeHalf: true);

        host.Services.GetRequiredService<ITextOutput>().ShouldBeOfType<TextOutput>();
    }

    [Fact]
    public void TheEventSimulatorResolvesAsASingleton()
    {
        using var host = BuildHost(withNativeHalf: true);

        var first = host.Services.GetRequiredService<IEventSimulator>();

        first.ShouldBeSameAs(host.Services.GetRequiredService<IEventSimulator>());
    }

    [Fact]
    public void OmittingTheNativeHalf_FailsAtBuildTimeNamingTheClipboard()
    {
        // Which is the whole reason the two halves are registered separately: this is a startup
        // failure that names the missing service, not a null reference at the first paste.
        var exception = Should.Throw<AggregateException>(() => BuildHost(withNativeHalf: false).Dispose());

        exception.ToString().ShouldContain(nameof(ISystemClipboard));
    }

    private static IHost BuildHost(bool withNativeHalf)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddTextOutput();

        if (withNativeHalf)
        {
            builder.Services.AddSingleton<ISystemClipboard, FakeClipboard>();
            builder.Services.AddSingleton<IPasteProbe, FakePasteProbe>();
        }

        return builder.Build();
    }
}
