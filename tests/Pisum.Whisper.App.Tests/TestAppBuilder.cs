namespace Pisum.Whisper.App.Tests;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

/// <summary>
/// The application every <c>[AvaloniaFact]</c> in this assembly runs against: a bare
/// <see cref="Application"/> carrying nothing but <see cref="FluentTheme"/>, which is all a window
/// needs to load and bind.
/// </summary>
/// <remarks>
/// <para>
/// It deliberately does not point at <see cref="App"/>. That constructor takes an
/// <see cref="IServiceProvider"/> and its <c>OnFrameworkInitializationCompleted</c> resolves a
/// dictation orchestrator and registers a native tray icon — none of which a view test needs and
/// none of which a headless platform provides.
/// </para>
/// <para>
/// The isolation level is left at its default of <c>PerTest</c>: a fresh application and dispatcher
/// per test method. <c>PerAssembly</c> is faster and documents itself as unsafe for tests that rely
/// on global state, which a settings window backed by a file-writing singleton is exactly.
/// </para>
/// </remarks>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .AfterSetup(builder => builder.Instance?.Styles.Add(new FluentTheme()));
    }
}
