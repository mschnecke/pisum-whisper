namespace Pisum.Whisper.App.Settings.ViewModels;

using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Logging;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Shell;
using Pisum.Whisper.Core.Transcription;

/// <summary>
/// The window's six tabs and the one <see cref="SettingsEditor"/> they share.
/// </summary>
/// <remarks>
/// The editor is shared rather than one per tab, so a quiet window covers whatever the user touched
/// in it — and so the Presets tab's flush covers every other tab's pending draft, which is the whole
/// point of that rule.
/// </remarks>
public sealed class SettingsWindowViewModel
{
    public SettingsWindowViewModel(SettingsStore store,
                                   SettingsEditor editor,
                                   IGeminiKeyProbe probe,
                                   IGlobalHotkeyService hotkeys,
                                   LogDirectory logs,
                                   ISystemShell shell,
                                   ILoggerFactory loggers)
    {
        Editor = editor;

        var settings = store.Current;

        Providers = new ProvidersViewModel(
            editor, probe, new ModelListCache(loggers.CreateLogger<ModelListCache>()), settings);
        Presets = new PresetsViewModel(store, editor, loggers.CreateLogger<PresetsViewModel>());
        Hotkey = new HotkeyViewModel(editor, hotkeys, loggers.CreateLogger<HotkeyViewModel>(), settings);
        Audio = new AudioViewModel(editor, settings);
        Logging = new LoggingViewModel(
            editor, logs, shell, loggers.CreateLogger<LoggingViewModel>(), settings);
        General = new GeneralViewModel(editor, settings);
    }

    public SettingsEditor Editor { get; }

    public ProvidersViewModel Providers { get; }

    public PresetsViewModel Presets { get; }

    public HotkeyViewModel Hotkey { get; }

    public AudioViewModel Audio { get; }

    public LoggingViewModel Logging { get; }

    public GeneralViewModel General { get; }
}
