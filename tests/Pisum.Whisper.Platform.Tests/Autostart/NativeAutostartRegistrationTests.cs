namespace Pisum.Whisper.Platform.Tests.Autostart;

using Microsoft.Extensions.DependencyInjection;
using Pisum.Whisper.Core.Autostart;
using Pisum.Whisper.Platform.Autostart;
using Shouldly;

/// <summary>
/// Task 4.4 — that <c>AddNativeAutostart</c> selects an implementation for the host operating system
/// and that it resolves, in <see cref="Shell.NativeShellRegistrationTests"/>' shape. Resolving it
/// reads no registry and writes no plist.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class NativeAutostartRegistrationTests
{
    [Fact]
    public void TheServiceResolves()
    {
        using var provider = new ServiceCollection().AddNativeAutostart().BuildServiceProvider();

        provider.GetRequiredService<IAutostartService>().ShouldNotBeNull();
    }

    [Fact]
    public void TheImplementationMatchesTheHostOperatingSystem()
    {
        using var provider = new ServiceCollection().AddNativeAutostart().BuildServiceProvider();

        var autostart = provider.GetRequiredService<IAutostartService>();

        if (OperatingSystem.IsWindows())
        {
            autostart.ShouldBeOfType<WindowsAutostart>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            autostart.ShouldBeOfType<MacOsAutostart>();
        }
    }
}
