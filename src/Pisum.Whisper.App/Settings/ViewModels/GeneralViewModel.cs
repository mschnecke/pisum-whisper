namespace Pisum.Whisper.App.Settings.ViewModels;

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// The General tab: the recording mode, the recording ceiling, and the two flags change 11 will act
/// on.
/// </summary>
/// <remarks>
/// <see cref="StartWithSystem"/> and <see cref="ShowTrayNotifications"/> persist and nothing consumes
/// them yet. They ship anyway because <see cref="AppSettings.StartWithSystem"/> already defaults to
/// <c>true</c> in a file no autostart code reads: the untruth exists whether or not the toggle does,
/// and the toggle at least lets the user record what they want before the code that honours it lands.
/// </remarks>
public sealed partial class GeneralViewModel : ObservableObject
{
    /// <summary>The shortest recording ceiling that is still a recording rather than a mistake.</summary>
    public const int MinimumDurationSecs = 10;

    /// <summary>An hour, matching the reference.</summary>
    public const int MaximumDurationSecs = 3600;

    private readonly SettingsEditor _editor;

    private RecordingMode _mode;

    [ObservableProperty]
    private string _maxRecordingDurationSecs;

    [ObservableProperty]
    private bool _startWithSystem;

    [ObservableProperty]
    private bool _showTrayNotifications;

    public GeneralViewModel(SettingsEditor editor, AppSettings settings)
    {
        _editor = editor;
        _mode = settings.RecordingMode;

        // Assigned to the fields rather than through the properties: the generated setters raise the
        // change hooks, which would write the values back to the store on the window's first open.
        _maxRecordingDurationSecs =
            settings.MaxRecordingDurationSecs.ToString(CultureInfo.InvariantCulture);
        _startWithSystem = settings.StartWithSystem;
        _showTrayNotifications = settings.ShowTrayNotifications;
    }

    public bool IsHoldToRecord
    {
        get => _mode == RecordingMode.HoldToRecord;
        set => Select(value, RecordingMode.HoldToRecord);
    }

    public bool IsToggle
    {
        get => _mode == RecordingMode.Toggle;
        set => Select(value, RecordingMode.Toggle);
    }

    partial void OnMaxRecordingDurationSecsChanged(string value)
    {
        var seconds = Bounded.Clamp(value, MinimumDurationSecs, MaximumDurationSecs);
        _editor.Edit(settings => settings.MaxRecordingDurationSecs = seconds);
    }

    partial void OnStartWithSystemChanged(bool value)
    {
        _editor.Edit(settings => settings.StartWithSystem = value);
    }

    partial void OnShowTrayNotificationsChanged(bool value)
    {
        _editor.Edit(settings => settings.ShowTrayNotifications = value);
    }

    private void Select(bool isChecked, RecordingMode mode)
    {
        if (!isChecked || _mode == mode)
        {
            return;
        }

        _mode = mode;
        OnPropertyChanged(nameof(IsHoldToRecord));
        OnPropertyChanged(nameof(IsToggle));

        _editor.Edit(settings => settings.RecordingMode = mode);
    }
}
