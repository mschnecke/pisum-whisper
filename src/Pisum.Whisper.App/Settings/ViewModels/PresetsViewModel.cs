namespace Pisum.Whisper.App.Settings.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Notifications;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// The Presets tab: the list, its two badges, and the four things that can be done to a preset.
/// </summary>
/// <remarks>
/// <para>
/// This tab is the one that does <b>not</b> write through <see cref="SettingsEditor"/>.
/// <see cref="SettingsStore"/> already exposes the three operations, and they encode rules the view
/// model must not duplicate: a built-in cannot be deleted, a deleted active preset moves to its
/// neighbor rather than back to the default, and a built-in's edited name and prompt survive the
/// next load's merge.
/// </para>
/// <para>
/// <b>Every command flushes the editor first.</b> Those three methods write and replace
/// <see cref="SettingsStore.Current"/>, so a draft cloned before a preset was added would be saved
/// after it and would silently delete it. One line per command, and one test per command asserting
/// it happened in that order.
/// </para>
/// <para>
/// <b>Each command's store call is wrapped in a <c>try</c>.</b> Writing to disk can fail — disk full,
/// permission denied, a network drive gone from under the home directory — and none of the three
/// <see cref="SettingsStore"/> methods below guards against it; a failure surfaces as
/// <see cref="SettingsException"/>, reported through <see cref="ReportSaveFailure"/> rather than
/// becoming an unobserved task exception the way it did before this existed.
/// </para>
/// <para>
/// Add and Save are disabled while either field is blank. <see cref="SettingsStore"/> validates
/// nothing, so an empty <see cref="Preset.SystemPrompt"/> would otherwise become selectable and be
/// sent to Gemini as the instruction for the user's speech.
/// </para>
/// </remarks>
public sealed partial class PresetsViewModel : ObservableObject
{
    private const string SaveFailureTitle = "Settings Not Saved";

    private readonly SettingsStore _store;

    private readonly SettingsEditor _editor;

    private readonly INotificationService _notifications;

    private readonly ILogger<PresetsViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private PresetEntryViewModel? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newSystemPrompt = string.Empty;

    public PresetsViewModel(SettingsStore store,
                            SettingsEditor editor,
                            INotificationService notifications,
                            ILogger<PresetsViewModel> logger)
    {
        _store = store;
        _editor = editor;
        _notifications = notifications;
        _logger = logger;

        Reload();
    }

    public ObservableCollection<PresetEntryViewModel> Presets { get; } = [];

    /// <summary>Whether the selected preset's name and prompt are both non-blank.</summary>
    public bool CanSaveSelected =>
        Selected is not null
        && !string.IsNullOrWhiteSpace(Selected.Name)
        && !string.IsNullOrWhiteSpace(Selected.SystemPrompt);

    /// <summary>Adds the preset typed into the two new-preset fields.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    public async Task AddAsync()
    {
        // The button is disabled for a blank field, and this is the same guard on the code path a
        // disabled button does not cover: CanExecute gates the button, not a direct invocation.
        if (!CanAdd())
        {
            return;
        }

        var preset = new Preset
        {
            Id = Guid.NewGuid().ToString(),
            Name = NewName.Trim(),
            SystemPrompt = NewSystemPrompt.Trim(),
        };

        await _editor.FlushAsync().ConfigureAwait(true);

        try
        {
            _store.SavePreset(preset);
        }
        catch (SettingsException exception)
        {
            ReportSaveFailure(exception, "Preset '{PresetName}' could not be added.", preset.Name);
            return;
        }

        // The name, never the prompt: a preset's system prompt is the user's own writing.
        _logger.LogInformation("Preset '{PresetName}' added.", preset.Name);

        NewName = string.Empty;
        NewSystemPrompt = string.Empty;
        Reload(preset.Id);
    }

    /// <summary>Writes the selected preset's edited name and prompt back.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveSelected))]
    public async Task SaveAsync()
    {
        if (!CanSaveSelected || Selected is null)
        {
            return;
        }

        var preset = new Preset
        {
            Id = Selected.Id,
            Name = Selected.Name.Trim(),
            SystemPrompt = Selected.SystemPrompt.Trim(),
            IsBuiltin = Selected.IsBuiltin,
        };

        await _editor.FlushAsync().ConfigureAwait(true);

        try
        {
            _store.SavePreset(preset);
        }
        catch (SettingsException exception)
        {
            ReportSaveFailure(exception, "Preset '{PresetName}' could not be saved.", preset.Name);
            return;
        }

        _logger.LogInformation("Preset '{PresetName}' saved.", preset.Name);
        Reload(preset.Id);
    }

    /// <summary>Makes the selected preset the one dictations are transcribed with.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public async Task ActivateAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var id = Selected.Id;

        await _editor.FlushAsync().ConfigureAwait(true);

        try
        {
            _store.SetActivePreset(id);
        }
        catch (SettingsException exception)
        {
            ReportSaveFailure(exception, "Preset {PresetId} could not be activated.", id);
            return;
        }

        _logger.LogInformation("Active preset is now {PresetId}.", id);
        Reload(id);
    }

    /// <summary>
    /// Deletes the selected preset, which is offered only when it is not built-in.
    /// </summary>
    /// <remarks>
    /// <see cref="SettingsStore.DeletePreset"/> also raises for a built-in, and this command cannot
    /// reach that throw: <see cref="CanDeleteSelected"/> is false for exactly the presets it refuses.
    /// The <c>try</c> below exists for the other way it can throw — a write that fails to reach
    /// disk — which no <c>CanExecute</c> guard can rule out.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    public async Task DeleteAsync()
    {
        if (!CanDeleteSelected() || Selected is null)
        {
            return;
        }

        var id = Selected.Id;

        await _editor.FlushAsync().ConfigureAwait(true);

        try
        {
            _store.DeletePreset(id);
        }
        catch (SettingsException exception)
        {
            ReportSaveFailure(exception, "Preset {PresetId} could not be deleted.", id);
            return;
        }

        _logger.LogInformation("Preset {PresetId} deleted.", id);
        Reload();
    }

    /// <summary>
    /// Rebuilds the list from the store, which is the authority after any of the three operations.
    /// </summary>
    private void Reload(string? select = null)
    {
        var settings = _store.Current;
        var wanted = select ?? Selected?.Id;

        Presets.Clear();
        foreach (var preset in settings.Presets)
        {
            Presets.Add(new PresetEntryViewModel(preset, preset.Id == settings.ActivePresetId));
        }

        Selected = Presets.FirstOrDefault(entry => entry.Id == wanted) ?? Presets.FirstOrDefault();
    }

    /// <summary>
    /// Logs and shows a failed write, then reloads so the tab reflects what is actually persisted
    /// rather than the edit that failed to reach it — the fix for a save that looked like it worked.
    /// </summary>
    private void ReportSaveFailure(SettingsException exception, string message, params object?[] args)
    {
        _logger.LogError(exception, message, args);
        _notifications.Notify(SaveFailureTitle, exception.Message);
        Reload();
    }

    private bool CanAdd()
    {
        return !string.IsNullOrWhiteSpace(NewName) && !string.IsNullOrWhiteSpace(NewSystemPrompt);
    }

    private bool HasSelection()
    {
        return Selected is not null;
    }

    private bool CanDeleteSelected()
    {
        return Selected is {IsBuiltin: false};
    }

    partial void OnSelectedChanged(PresetEntryViewModel? oldValue, PresetEntryViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedFieldChanged;
        }

        if (newValue is not null)
        {
            // The Save guard reads the selected entry's own fields, so it has to hear them change.
            newValue.PropertyChanged += OnSelectedFieldChanged;
        }

        OnPropertyChanged(nameof(CanSaveSelected));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectedFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PresetEntryViewModel.Name)
            or nameof(PresetEntryViewModel.SystemPrompt))
        {
            OnPropertyChanged(nameof(CanSaveSelected));
            SaveCommand.NotifyCanExecuteChanged();
        }
    }
}
