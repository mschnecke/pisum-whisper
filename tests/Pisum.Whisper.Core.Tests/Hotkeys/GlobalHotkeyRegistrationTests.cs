namespace Pisum.Whisper.Core.Tests.Hotkeys;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Settings;
using SharpHook.Logging;
using SharpHook.Providers;
using SharpHook.Testing;
using Shouldly;

/// <summary>
/// Task 4.1 — the registration itself, exercised rather than reconstructed.
/// </summary>
[IntegrationTest]
public sealed class GlobalHotkeyRegistrationTests : IDisposable
{
    private string _home = string.Empty;

    public GlobalHotkeyRegistrationTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        Directory.Delete(_home, true);
    }

    [Fact]
    public void AllThreeRolesResolveToOneInstance()
    {
        using var host = BuildHost();

        var concrete = host.Services.GetRequiredService<GlobalHotkeyService>();
        var contract = host.Services.GetRequiredService<IGlobalHotkeyService>();
        var hosted = host.Services.GetServices<IHostedService>().OfType<GlobalHotkeyService>().Single();

        // libuiohook keeps one static callback per process: a second concurrent hook corrupts its
        // internal state, so this is a correctness constraint, not tidiness.
        contract.ShouldBeSameAs(concrete);
        hosted.ShouldBeSameAs(concrete);
    }

    [Fact]
    public void TheRegistrationSatisfiesContainerValidation()
    {
        // The application builds its container with ValidateOnBuild, so an unsatisfiable dependency
        // here is a startup failure rather than a null reference at first use.
        Should.NotThrow(() => BuildHost(validate: true).Dispose());
    }

    [Fact]
    public void TheServiceIsResolvedBeforeTheHookStarts()
    {
        using var host = BuildHost();

        var service = host.Services.GetRequiredService<IGlobalHotkeyService>();

        service.Availability.ShouldBe(HotkeyAvailability.NotStarted);
        service.Chord.ShouldBe(HotkeyChord.Default);
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

        builder.Services.AddGlobalHotkey();

        // The real registration resolves the native provider and a native log source. Both are
        // replaced here so a unit test does not install a machine-wide hook.
        builder.Services.AddSingleton<ILogSource>(_ => new EmptyLogSource());
        builder.Services.AddSingleton<IGlobalHookProvider>(_ => new TestProvider(TestThreadingMode.Simple));
        builder.Services.AddSingleton(provider => new GlobalHotkeyService(
            NullLogger<GlobalHotkeyService>.Instance,
            provider.GetRequiredService<SettingsStore>(),
            provider.GetRequiredService<ILogSource>(),
            provider.GetRequiredService<IGlobalHookProvider>()));

        return builder.Build();
    }
}
