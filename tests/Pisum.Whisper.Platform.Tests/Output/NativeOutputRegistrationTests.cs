namespace Pisum.Whisper.Platform.Tests.Output;

using Microsoft.Extensions.DependencyInjection;
using Pisum.Whisper.Core.Output;
using Pisum.Whisper.Platform.Output;
using Shouldly;

/// <summary>
/// Task 3.8 — that <c>AddNativeOutput</c> selects an implementation for the host operating system
/// and that both halves resolve. Constructing them touches no clipboard and posts no input, so this
/// runs anywhere the suite runs.
/// </summary>
[TestClass]
public sealed class NativeOutputRegistrationTests
{
    [TestMethod]
    public void BothHalvesResolveOnThisPlatform()
    {
        using var provider = new ServiceCollection().AddNativeOutput().BuildServiceProvider();

        provider.GetRequiredService<ISystemClipboard>().ShouldNotBeNull();
        provider.GetRequiredService<IPasteProbe>().ShouldNotBeNull();
    }

    [TestMethod]
    public void TheImplementationsMatchTheHostOperatingSystem()
    {
        using var provider = new ServiceCollection().AddNativeOutput().BuildServiceProvider();

        var clipboard = provider.GetRequiredService<ISystemClipboard>();
        var probe = provider.GetRequiredService<IPasteProbe>();

        if (OperatingSystem.IsWindows())
        {
            clipboard.ShouldBeOfType<WindowsClipboard>();
            probe.ShouldBeOfType<WindowsPasteProbe>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            clipboard.ShouldBeOfType<MacOsClipboard>();
            probe.ShouldBeOfType<MacOsPasteProbe>();
        }
    }
}
