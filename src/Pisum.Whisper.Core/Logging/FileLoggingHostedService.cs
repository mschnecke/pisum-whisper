namespace Pisum.Whisper.Core.Logging;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Settings;
using Serilog.Core;

/// <summary>
/// Keeps the log level in step with settings, so a user reproducing a problem can raise verbosity
/// mid-session — restarting to change the level destroys the state that caused the problem — and
/// reports at shutdown what the asynchronous buffer dropped.
/// </summary>
/// <remarks>
/// A hosted service rather than a singleton, because <c>ValidateOnBuild</c> validates registrations
/// without instantiating them: a singleton nobody resolves never subscribes. <c>host.Start()</c> is
/// what guarantees this one exists.
/// </remarks>
internal sealed class FileLoggingHostedService : IHostedService
{
    private readonly SettingsStore _settings;
    private readonly LoggingLevelSwitch _levelSwitch;
    private readonly DroppedLogEventMonitor _monitor;
    private readonly ILogger<FileLoggingHostedService> _logger;

    public FileLoggingHostedService(
        SettingsStore settings,
        LoggingLevelSwitch levelSwitch,
        DroppedLogEventMonitor monitor,
        ILogger<FileLoggingHostedService> logger)
    {
        _settings = settings;
        _levelSwitch = levelSwitch;
        _monitor = monitor;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The initial level comes from the loaded settings rather than from the event, because
        // Changed is raised only from Save.
        Apply(_settings.Current.LoggingConfig.LogLevel);
        _settings.Changed += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _settings.Changed -= OnSettingsChanged;

        var dropped = _monitor.DroppedMessagesCount;
        if (dropped > 0)
        {
            _logger.LogWarning(
                "The log buffer dropped {DroppedLogEventCount} events, so this log is incomplete.",
                dropped);
        }

        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        Apply(settings.LoggingConfig.LogLevel);

    private void Apply(string name)
    {
        if (!LogLevelNames.TryParse(name, out var level))
        {
            _logger.LogWarning(
                "Log level '{LogLevel}' is not one of trace, debug, info, warn or error; using info.",
                name);
        }

        _levelSwitch.MinimumLevel = level;
    }
}
