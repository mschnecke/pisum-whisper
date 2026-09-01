namespace Pisum.Whisper.App.Settings.ViewModels;

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Shell;

/// <summary>
/// The Logging tab: the level, the rotation size, the retention window, where the files are, and a
/// button that opens that directory.
/// </summary>
/// <remarks>
/// The level applies immediately, through level switch change 3 registered. The size and the
/// retention do not: both are read in <c>AddFileLogging</c>, before the container exists, so they
/// take effect at the next launch — and the view says so. Saying it is not decoration, because every
/// other field in this window applying instantly teaches the user that they all do.
/// </remarks>
public sealed partial class LoggingViewModel : ObservableObject
{
    /// <summary>The five names <see cref="LogLevelNames"/> parses, in increasing severity.</summary>
    public static readonly IReadOnlyList<string> Levels = ["trace", "debug", "info", "warn", "error"];

    public const int MinimumFileSizeMb = 1;

    public const int MaximumFileSizeMb = 100;

    public const int MinimumRetentionDays = 1;

    public const int MaximumRetentionDays = 365;

    private readonly SettingsEditor _editor;

    private readonly ISystemShell _shell;

    private readonly ILogger<LoggingViewModel> _logger;

    [ObservableProperty]
    private string _logLevel;

    [ObservableProperty]
    private string _maxFileSizeMb;

    [ObservableProperty]
    private string _retentionDays;

    [ObservableProperty]
    private string? _openFailure;

    public LoggingViewModel(SettingsEditor editor,
                            LogDirectory logs,
                            ISystemShell shell,
                            ILogger<LoggingViewModel> logger,
                            AppSettings settings)
    {
        _editor = editor;
        _shell = shell;
        _logger = logger;
        LogDirectoryPath = logs.Path;

        var config = settings.LoggingConfig;
        _logLevel = Levels.Contains(config.LogLevel, StringComparer.OrdinalIgnoreCase)
            ? config.LogLevel.ToLowerInvariant()
            : "info";
        _maxFileSizeMb = config.LogMaxFileSizeMb.ToString(CultureInfo.InvariantCulture);
        _retentionDays = config.LogRetentionDays.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The directory the log files are written to, shown in full.</summary>
    public string LogDirectoryPath { get; }

    public IReadOnlyList<string> LogLevels => Levels;

    /// <summary>
    /// Opens the log directory in the operating system's file browser.
    /// </summary>
    /// <remarks>
    /// A failure is reported beside the button, and nothing else happens: a file browser that will not
    /// start is no reason for the window to stop working.
    /// </remarks>
    [RelayCommand]
    public void OpenLogFolder()
    {
        OpenFailure = null;

        try
        {
            _shell.OpenFolder(LogDirectoryPath);
        }
        catch (SystemShellException exception)
        {
            _logger.LogWarning(exception, "The log folder could not be opened.");
            OpenFailure = exception.Message;
        }
    }

    partial void OnLogLevelChanged(string value)
    {
        _editor.Edit(settings => settings.LoggingConfig.LogLevel = value);
    }

    partial void OnMaxFileSizeMbChanged(string value)
    {
        var megabytes = Bounded.Clamp(value, MinimumFileSizeMb, MaximumFileSizeMb);
        _editor.Edit(settings => settings.LoggingConfig.LogMaxFileSizeMb = megabytes);
    }

    partial void OnRetentionDaysChanged(string value)
    {
        var days = Bounded.Clamp(value, MinimumRetentionDays, MaximumRetentionDays);
        _editor.Edit(settings => settings.LoggingConfig.LogRetentionDays = days);
    }
}
