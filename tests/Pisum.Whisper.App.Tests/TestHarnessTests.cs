namespace Pisum.Whisper.App.Tests;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Shouldly;

/// <summary>
/// Task 1.2 and task 1.4 — that this assembly is discovered and run by the Microsoft Testing
/// Platform, and that <see cref="TestAppBuilder"/> stands up an application a window can be shown in.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TestHarnessTests
{
    [Fact]
    public void TheAssemblyIsDiscoveredAndRun()
    {
        true.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void AWindowCanBeShown()
    {
        var window = new Window();

        window.Show();

        window.IsVisible.ShouldBeTrue();
    }
}
