namespace Pisum.Whisper.Core.Autostart;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// Makes the machine's login registration agree with <see cref="AppSettings.StartWithSystem"/>, at
/// startup and after every save.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reconciled rather than toggled.</b> The obvious alternative is to call
/// <see cref="IAutostartService.Enable"/> from the General tab's switch. It is the same amount of
/// code and covers less: reconciling covers the first-launch enable, the toggle, a settings file
/// edited by hand, and a registration some other tool removed, through one path with one test, where
/// the view-model version needs a second path for first launch and silently diverges the other two.
/// It is cheap, too — <see cref="SettingsStore.Changed"/> fires once per debounced commit, not once
/// per keystroke, so this is one registry read per save.
/// </para>
/// <para>
/// <b>It reads before it writes, and that is deliberate.</b>
/// <c>GlobalHotkeyService.OnSettingsChanged</c> logs a rebind outside <c>HotkeyMatcher.Rebind</c>'s
/// early return, so changing the audio format writes a misleading line at the default level. Here
/// the same mistake would be a registry mutation rather than a no-op, so the comparison happens
/// first and nothing is written or logged when the two already agree.
/// </para>
/// <para>
/// <b>What it compares is not whether a registration exists.</b> It did once, and the state that
/// survived was the one this exists to prevent: a registration naming an executable that is no
/// longer this one — an install over a build a developer had been running, or an application moved —
/// agreed with an enabled setting on the only question being asked, so it was never rewritten and
/// the machine went on launching the old path at every login. <see cref="IAutostartService.Read"/>
/// answers with <see cref="AutostartRegistration"/> instead, and only
/// <see cref="AutostartRegistration.Current"/> is agreement.
/// </para>
/// <para>
/// <b>A failure never stops the application starting.</b> A machine policy, a locked registry key or
/// an unwritable home directory is a reason to lose autostart, not a reason to lose the dictation
/// hotkey.
/// </para>
/// </remarks>
public sealed class AutostartReconciler : IHostedService, IDisposable
{
    private readonly ILogger<AutostartReconciler> _logger;

    private readonly SettingsStore _settings;

    private readonly IAutostartService _autostart;

    private bool _disposed;

    public AutostartReconciler(ILogger<AutostartReconciler> logger,
                               SettingsStore settings,
                               IAutostartService autostart)
    {
        _logger = logger;
        _settings = settings;
        _autostart = autostart;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribed before the first reconcile, so a save that lands during it is not missed.
        _settings.Changed += OnSettingsChanged;

        Reconcile(_settings.Current);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _settings.Changed -= OnSettingsChanged;

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Reconcile(settings);
    }

    private void Reconcile(AppSettings settings)
    {
        var wanted = settings.StartWithSystem;

        try
        {
            var registration = _autostart.Read();

            if (registration == Agreement(wanted))
            {
                // Nothing is written and nothing is logged: this is the ordinary case, once per save.
                return;
            }

            if (wanted)
            {
                // Enable overwrites, so this is the one call for both "nothing is registered" and
                // "something else is". Which of the two it was is what the log line distinguishes.
                _autostart.Enable();
            }
            else
            {
                _autostart.Disable();
            }

            _logger.LogInformation(
                "Start at login was {Action} to match the setting.",
                Action(wanted, registration));
        }
        catch (AutostartException exception)
        {
            _logger.LogError(
                exception,
                "Start at login could not be brought to {Wanted}; the application continues without it.",
                wanted);
        }
    }

    /// <summary>The one registration state that needs no write, for a given setting.</summary>
    private static AutostartRegistration Agreement(bool wanted)
    {
        return wanted ? AutostartRegistration.Current : AutostartRegistration.Absent;
    }

    /// <summary>
    /// What was actually done, in three words rather than two. A registration repointed at this
    /// executable is not the same event as one created, and reporting both as "enabled" would leave
    /// the log unable to say that anything had been wrong.
    /// </summary>
    private static string Action(bool wanted, AutostartRegistration registration)
    {
        if (!wanted)
        {
            return "disabled";
        }

        return registration == AutostartRegistration.Stale ? "repointed at this executable" : "enabled";
    }
}
