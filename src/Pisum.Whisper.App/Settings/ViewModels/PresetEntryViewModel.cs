namespace Pisum.Whisper.App.Settings.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// One preset in the list: its name and prompt as they are being edited, plus the two badges.
/// </summary>
/// <remarks>
/// Like <see cref="ProviderEntryViewModel"/> it holds an <see cref="Id"/> and never a
/// <see cref="Preset"/> out of the published graph. This tab writes through
/// <c>SettingsStore</c> rather than <see cref="SettingsEditor"/>, but the reason is the same: every
/// write replaces the graph.
/// </remarks>
public sealed partial class PresetEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _systemPrompt;

    [ObservableProperty]
    private bool _isActive;

    public PresetEntryViewModel(Preset preset, bool isActive)
    {
        Id = preset.Id;
        IsBuiltin = preset.IsBuiltin;
        _name = preset.Name;
        _systemPrompt = preset.SystemPrompt;
        _isActive = isActive;
    }

    public string Id { get; }

    /// <summary>Whether this preset ships with the application, which is what forbids deleting it.</summary>
    public bool IsBuiltin { get; }

    /// <summary>The inverse of <see cref="IsBuiltin"/>, so the view can hide the delete control.</summary>
    public bool CanDelete => !IsBuiltin;
}
