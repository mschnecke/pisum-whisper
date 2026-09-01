namespace Pisum.Whisper.Core.Notifications;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the notification policy.
/// </summary>
/// <remarks>
/// The transport — <see cref="INotificationPresenter"/> — is registered separately by the
/// application, in the shape of <c>AddTextOutput</c> plus <c>AddNativeOutput</c>. With
/// <c>ValidateOnBuild</c> on, omitting it is a startup failure naming
/// <see cref="INotificationPresenter"/> rather than a null reference at the first error a user hits.
/// </remarks>
public static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddSingleton<INotificationService, NotificationService>();

        return services;
    }
}
