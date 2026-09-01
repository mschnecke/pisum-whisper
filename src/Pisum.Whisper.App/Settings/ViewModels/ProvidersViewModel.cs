namespace Pisum.Whisper.App.Settings.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;

/// <summary>
/// The Providers tab: the list of Gemini entries, and adding and removing them.
/// </summary>
/// <remarks>
/// An entry with an empty API key is allowed through even when enabled, as in the reference. It is
/// not an oversight: <c>GeminiProviderPool</c> already reports it as a configuration failure naming
/// the problem, which is a better diagnosis than a disabled control that does not say why.
/// </remarks>
public sealed partial class ProvidersViewModel : ObservableObject
{
    private readonly SettingsEditor _editor;

    private readonly IGeminiKeyProbe _probe;

    private readonly ModelListCache _models;

    public ProvidersViewModel(SettingsEditor editor,
                              IGeminiKeyProbe probe,
                              ModelListCache models,
                              AppSettings settings)
    {
        _editor = editor;
        _probe = probe;
        _models = models;

        foreach (var entry in settings.Providers)
        {
            Entries.Add(NewEntry(entry.Id, entry.ApiKey, entry.Model, entry.Enabled));
        }
    }

    public ObservableCollection<ProviderEntryViewModel> Entries { get; } = [];

    /// <summary>Adds an empty enabled entry, whose id is what every later edit finds it by.</summary>
    [RelayCommand]
    public void Add()
    {
        // A GUID, matching the reference's crypto.randomUUID().
        var id = Guid.NewGuid().ToString();

        _editor.Edit(settings => settings.Providers.Add(
            new ProviderConfig {Id = id, ApiKey = string.Empty, Model = null, Enabled = true}));

        Entries.Add(NewEntry(id, string.Empty, null, true));
    }

    [RelayCommand]
    public void Remove(ProviderEntryViewModel entry)
    {
        var id = entry.Id;

        _editor.Edit(settings => settings.Providers.RemoveAll(candidate => candidate.Id == id));

        Entries.Remove(entry);
    }

    private ProviderEntryViewModel NewEntry(string id, string apiKey, string? model, bool enabled)
    {
        return new ProviderEntryViewModel(_editor, _probe, _models, id, apiKey, model, enabled);
    }
}
