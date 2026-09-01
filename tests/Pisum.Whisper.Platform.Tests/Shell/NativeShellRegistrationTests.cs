namespace Pisum.Whisper.Platform.Tests.Shell;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisum.Whisper.Core.Shell;
using Pisum.Whisper.Platform.Shell;
using Shouldly;

/// <summary>
/// Task 1.6 — that <c>AddNativeShell</c> satisfies container validation and resolves, in the shape
/// of <see cref="Output.NativeOutputRegistrationTests"/>. Resolving it starts no process.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class NativeShellRegistrationTests
{
    [Fact]
    public void TheRegistrationSatisfiesContainerValidation()
    {
        Should.NotThrow(() => BuildHost().Dispose());
    }

    [Fact]
    public void TheShellResolves()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<ISystemShell>().ShouldBeOfType<SystemShell>();
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddNativeShell();

        return builder.Build();
    }
}
