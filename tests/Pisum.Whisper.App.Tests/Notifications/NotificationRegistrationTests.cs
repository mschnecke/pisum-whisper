namespace Pisum.Whisper.App.Tests.Notifications;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.App.Notifications;
using Pisum.Whisper.Core.Notifications;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>
/// Task 2.3 — that both halves of the notification capability are registered, in
/// <see cref="Settings.SettingsWindowRegistrationTests"/>' shape.
/// </summary>
/// <remarks>
/// The halves are registered separately on purpose. With <c>ValidateOnBuild</c> on, omitting the
/// presenter is a startup failure naming <see cref="INotificationPresenter"/> rather than a null
/// reference at the first error a user hits — which is a place the user is by definition already
/// having a bad time.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class NotificationRegistrationTests : IDisposable
{
    private readonly string _home;

    public NotificationRegistrationTests()
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
    public void BothHalvesResolve()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<INotificationService>().ShouldNotBeNull();
        host.Services.GetRequiredService<INotificationPresenter>().ShouldBeOfType<ToastPresenter>();
    }

    /// <summary>
    /// The interface and the concrete type are one object, so <c>App.OnExit</c> closes the very
    /// notifications the pipeline put on screen rather than a second, empty presenter's.
    /// </summary>
    [Fact]
    public void ThePresenterIsOneSingletonUnderBothOfItsRoles()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<INotificationPresenter>()
            .ShouldBeSameAs(host.Services.GetRequiredService<ToastPresenter>());
    }

    /// <summary>
    /// Omitting the presenter must name it. This is the failure the two-half registration exists to
    /// produce, so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void OmittingThePresenterIsAStartupFailureThatNamesIt()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddSingleton(provider => new SettingsStore(
            provider.GetRequiredService<ILogger<SettingsStore>>(),
            Path.Combine(_home, ".pisum-whisper.json")));
        builder.Services.AddNotifications();

        Should.Throw<AggregateException>(() => builder.Build())
            .ToString()
            .ShouldContain(nameof(INotificationPresenter));
    }

    private IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddSingleton(provider => new SettingsStore(
            provider.GetRequiredService<ILogger<SettingsStore>>(),
            Path.Combine(_home, ".pisum-whisper.json")));

        builder.Services.AddNotifications();
        builder.Services.AddSingleton<ToastPresenter>();
        builder.Services.AddSingleton<INotificationPresenter>(
            provider => provider.GetRequiredService<ToastPresenter>());

        return builder.Build();
    }
}
