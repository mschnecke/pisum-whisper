namespace Pisum.Whisper.App.Tests.Settings;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.App.Notifications;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Notifications;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Shell;
using Pisum.Whisper.Core.Transcription;
using Pisum.Whisper.Platform.Shell;
using Shouldly;

/// <summary>
/// Task 3.7 — that everything the settings window resolves is registered, in
/// <c>TextOutputRegistrationTests</c>' shape.
/// </summary>
/// <remarks>
/// With <c>ValidateOnBuild</c> on, a missing registration is a startup failure that names the
/// service rather than a null reference the first time the user opens the window.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class SettingsWindowRegistrationTests : IDisposable
{
    private readonly string _home;

    public SettingsWindowRegistrationTests()
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
        Should.NotThrow(() => BuildHost().Dispose());
    }

    [Fact]
    public void EverythingTheWindowNeedsResolves()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<SettingsStore>().ShouldNotBeNull();
        host.Services.GetRequiredService<SettingsEditor>().ShouldNotBeNull();
        host.Services.GetRequiredService<IGeminiKeyProbe>().ShouldNotBeNull();
        host.Services.GetRequiredService<IGlobalHotkeyService>().ShouldNotBeNull();
        host.Services.GetRequiredService<LogDirectory>().ShouldNotBeNull();
        host.Services.GetRequiredService<ISystemShell>().ShouldNotBeNull();
        host.Services.GetRequiredService<INotificationService>().ShouldNotBeNull();
        host.Services.GetRequiredService<SettingsWindowViewModel>().ShouldNotBeNull();
    }

    [Fact]
    public void TheEditorIsASingleton_SoOneFlushCoversEveryTab()
    {
        using var host = BuildHost();

        var editor = host.Services.GetRequiredService<SettingsEditor>();

        editor.ShouldBeSameAs(host.Services.GetRequiredService<SettingsEditor>());
        host.Services.GetRequiredService<SettingsWindowViewModel>().Editor.ShouldBeSameAs(editor);
    }

    private IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddFileLogging(
            new FileLoggingOptions {Directory = new LogDirectory(Path.Combine(_home, "logs"))}, out _);
        builder.Services.AddSingleton(provider => new SettingsStore(
            provider.GetRequiredService<ILogger<SettingsStore>>(),
            Path.Combine(_home, ".pisum-whisper.json")));
        builder.Services.AddGeminiTranscription();
        builder.Services.AddGlobalHotkey();
        builder.Services.AddNativeShell();
        builder.Services.AddNotifications();
        builder.Services.AddSingleton<ToastPresenter>();
        builder.Services.AddSingleton<INotificationPresenter>(
            provider => provider.GetRequiredService<ToastPresenter>());
        builder.Services.AddSingleton<SettingsEditor>();
        builder.Services.AddSingleton<SettingsWindowViewModel>();

        return builder.Build();
    }
}
