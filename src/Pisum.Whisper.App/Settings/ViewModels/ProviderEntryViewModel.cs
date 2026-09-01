namespace Pisum.Whisper.App.Settings.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pisum.Whisper.Core.Transcription;

/// <summary>
/// One Gemini entry: its key, its model, whether it is enabled, and the two things that can be asked
/// of a key before a dictation depends on it.
/// </summary>
/// <remarks>
/// <para>
/// It holds its entry's <see cref="Id"/> and never a <c>ProviderConfig</c>. That is the invariant
/// <see cref="SettingsEditor.Edit"/> documents, and this is where breaking it is easiest: an entry
/// view model naturally wants to hold its own model object, and a commit replaces the graph that
/// object belongs to, so every edit after the first would be written into a graph nothing saves.
/// </para>
/// <para>
/// The key is masked by default. It is never logged, at any level, and the failure text rendered
/// here comes from <see cref="KeyProbeResult"/>, which the probe has already scrubbed.
/// </para>
/// </remarks>
public sealed partial class ProviderEntryViewModel : ObservableObject
{
    /// <summary>The dropdown's an empty option, which leaves the provider on its own default.</summary>
    public static readonly GeminiModel DefaultModelOption =
        new(string.Empty, $"Default ({GeminiDefaults.Model})");

    private readonly SettingsEditor _editor;

    private readonly IGeminiKeyProbe _probe;

    private readonly ModelListCache _models;

    [ObservableProperty]
    private string _apiKey;

    [ObservableProperty]
    private bool _enabled;

    /// <summary>
    /// Whether the key box shows its characters. False by default: the window is displayed on a
    /// screen other people can see.
    /// </summary>
    [ObservableProperty]
    private bool _isKeyRevealed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadModelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshModelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private GeminiModel _selectedModel;

    [ObservableProperty]
    private string? _testResult;

    [ObservableProperty]
    private bool _testSucceeded;

    public ProviderEntryViewModel(SettingsEditor editor,
                                  IGeminiKeyProbe probe,
                                  ModelListCache models,
                                  string id,
                                  string apiKey,
                                  string? model,
                                  bool enabled)
    {
        _editor = editor;
        _probe = probe;
        _models = models;
        Id = id;

        // Seeded into the fields, not through the generated setters: those raise the change hooks,
        // which would write every displayed value back to the store when the window opens.
        _apiKey = apiKey;
        _enabled = enabled;
        _selectedModel = ModelOptionFor(model);

        Models.Add(DefaultModelOption);
        if (_selectedModel != DefaultModelOption)
        {
            Models.Add(_selectedModel);
        }
    }

    /// <summary>The settings entry in this view model edits. The only handle it keeps on it.</summary>
    public string Id { get; }

    /// <summary>The models offered for selection, the empty default first.</summary>
    public ObservableCollection<GeminiModel> Models { get; } = [];

    /// <summary>Whether a key has been entered, which is what the two probe commands need.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// Fetches the models this key may use, served from the window's cache when the same key has
    /// already been listed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanProbe))]
    public async Task LoadModelsAsync(CancellationToken cancellationToken)
    {
        await FetchModelsAsync(false, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Clears this key's cached listing and fetches it again.</summary>
    [RelayCommand(CanExecute = nameof(CanProbe))]
    public async Task RefreshModelsAsync(CancellationToken cancellationToken)
    {
        await FetchModelsAsync(true, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Checks the key and model against Gemini and renders the outcome beside the button.
    /// </summary>
    /// <remarks>
    /// No <c>try</c> around the call: <see cref="KeyProbeResult"/> exists precisely so that a failed
    /// test renders without one.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanProbe))]
    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        TestResult = "Testing...";
        TestSucceeded = false;

        try
        {
            var model = string.IsNullOrEmpty(SelectedModel.Id) ? null : SelectedModel.Id;
            var result = await _probe.TestConnectionAsync(ApiKey, model, cancellationToken)
                .ConfigureAwait(true);

            TestSucceeded = result.Succeeded;
            TestResult = result.Category is null
                ? result.Message
                : $"{result.Message} ({result.Category})";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanProbe()
    {
        return HasApiKey && !IsBusy;
    }

    private static GeminiModel ModelOptionFor(string? model)
    {
        return string.IsNullOrWhiteSpace(model) ? DefaultModelOption : new GeminiModel(model, model);
    }

    private async Task FetchModelsAsync(bool refresh, CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var key = ApiKey;
            if (refresh)
            {
                _models.Forget(key);
            }

            var listed = await _models.GetAsync(key, _probe, cancellationToken).ConfigureAwait(true);
            var selected = SelectedModel;

            Models.Clear();
            Models.Add(DefaultModelOption);
            foreach (var model in listed)
            {
                Models.Add(model);
            }

            // A listing that does not offer the configured model must not silently switch the user
            // to the default, so the current choice is kept in the list either way.
            if (selected != DefaultModelOption && Models.All(model => model.Id != selected.Id))
            {
                Models.Add(selected);
            }

            SelectedModel = Models.FirstOrDefault(model => model.Id == selected.Id) ?? DefaultModelOption;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnApiKeyChanged(string value)
    {
        OnPropertyChanged(nameof(HasApiKey));
        LoadModelsCommand.NotifyCanExecuteChanged();
        RefreshModelsCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();

        var id = Id;
        _editor.Edit(settings =>
        {
            // By id, inside the settings handed in. FirstOrDefault and return, never First: a removal
            // and a keystroke can land in the same quiet window.
            var entry = settings.Providers.FirstOrDefault(candidate => candidate.Id == id);
            if (entry is not null)
            {
                entry.ApiKey = value;
            }
        });
    }

    partial void OnEnabledChanged(bool value)
    {
        var id = Id;
        _editor.Edit(settings =>
        {
            var entry = settings.Providers.FirstOrDefault(candidate => candidate.Id == id);
            if (entry is not null)
            {
                entry.Enabled = value;
            }
        });
    }

    partial void OnSelectedModelChanged(GeminiModel value)
    {
        var id = Id;
        var model = string.IsNullOrEmpty(value.Id) ? null : value.Id;

        _editor.Edit(settings =>
        {
            var entry = settings.Providers.FirstOrDefault(candidate => candidate.Id == id);
            if (entry is not null)
            {
                entry.Model = model;
            }
        });
    }
}
