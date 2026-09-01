namespace Pisum.Whisper.App.Settings.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// The Audio tab: the format a recording is encoded in, and nothing else.
/// </summary>
/// <remarks>
/// The two choices are exposed as a pair of booleans because that is what a radio button binds to.
/// They are views of one field rather than two pieces of state, so they cannot disagree.
/// </remarks>
public sealed partial class AudioViewModel : ObservableObject
{
    private readonly SettingsEditor _editor;

    private AudioFormat _format;

    public AudioViewModel(SettingsEditor editor, AppSettings settings)
    {
        _editor = editor;
        _format = settings.AudioFormat;
    }

    public bool IsOpus
    {
        get => _format == AudioFormat.Opus;
        set => Select(value, AudioFormat.Opus);
    }

    public bool IsWav
    {
        get => _format == AudioFormat.Wav;
        set => Select(value, AudioFormat.Wav);
    }

    /// <summary>
    /// Applies a radio button being checked. An unchecking is ignored: the other button's check is
    /// the event that carries the user's choice, and acting on both would write twice.
    /// </summary>
    private void Select(bool isChecked, AudioFormat format)
    {
        if (!isChecked || _format == format)
        {
            return;
        }

        _format = format;
        OnPropertyChanged(nameof(IsOpus));
        OnPropertyChanged(nameof(IsWav));

        _editor.Edit(settings => settings.AudioFormat = format);
    }
}
