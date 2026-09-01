namespace Pisum.Whisper.App.Settings.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Settings;

/// <summary>
/// The Hotkey tab: the current binding, and a recorder that captures a new one by having the user
/// press it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IGlobalHotkeyService.CaptureAsync"/> does the capturing. Three rules it deliberately
/// leaves to its caller live here: a capture with no modifier is refused, and recording continues; a
/// captured bare <c>Escape</c> is the cancel — read from the capture rather than from a key event,
/// because both the hook and the focused window see the keystroke in no guaranteed order; and
/// <see cref="HotkeyCaptureOutcome.KeyNotSupported"/> is rendered as a message with recording still
/// running.
/// </para>
/// <para>
/// An open capture suspends hotkey matching process-wide, so <see cref="Cancel"/> is also called
/// when the window hides and when it is <b>deactivated</b>. Without the last one, a user who clicks
/// Change and then switches to another application has silently disabled their hotkey for the rest
/// of the session with nothing saying so.
/// </para>
/// <para>
/// Nothing about a key is logged but the accepted chord and the outcome. This view model sits on the
/// one-code path that observes every key on the machine, and the window's own Open Log Folder button
/// puts the log file one click away.
/// </para>
/// </remarks>
public sealed partial class HotkeyViewModel : ObservableObject
{
    private readonly SettingsEditor _editor;

    private readonly IGlobalHotkeyService _hotkeys;

    private readonly ILogger<HotkeyViewModel> _logger;

    private CancellationTokenSource? _capture;

    [ObservableProperty]
    private string _binding;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelRecordingCommand))]
    private bool _isRecording;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _conflictsWithSystemHotkey;

    public HotkeyViewModel(SettingsEditor editor,
                           IGlobalHotkeyService hotkeys,
                           ILogger<HotkeyViewModel> logger,
                           AppSettings settings)
    {
        _editor = editor;
        _hotkeys = hotkeys;
        _logger = logger;

        _binding = Describe(settings.Hotkey);
        _conflictsWithSystemHotkey = ConflictDetector.ConflictsWithSystemHotkey(settings.Hotkey);
    }

    /// <summary>Whether keys are being observed at all, and if not, why.</summary>
    public HotkeyAvailability Availability => _hotkeys.Availability;

    /// <summary>Whether the recorder can be offered. It cannot when nothing is observing keys.</summary>
    public bool IsAvailable => Availability == HotkeyAvailability.Available;

    /// <summary>
    /// The banner shown when the hook is not running, or <c>null</c> when it is.
    /// </summary>
    /// <remarks>
    /// With <see cref="HotkeyAvailability.Failed"/> a capture would never complete, so the recorder
    /// would sit on "Press a key combination..." forever. This is what stops that, and it is a
    /// smaller thing than telling a user who never opens this window.
    /// </remarks>
    public string? UnavailableBanner =>
        Availability switch
        {
            HotkeyAvailability.Available => null,
            HotkeyAvailability.NotStarted =>
                "Keys are not being observed yet, so a new combination cannot be recorded.",
            HotkeyAvailability.PermissionNotGranted =>
                "Pisum Whisper has not been allowed to observe keys system-wide. Grant it accessibility "
                + "access and start the application again.",
            HotkeyAvailability.PermissionRevoked =>
                "Permission to observe keys system-wide was withdrawn. Grant it again and start the "
                + "application again.",
            _ => "Keys could not be observed, so the hotkey does not work and cannot be re-recorded.",
        };

    /// <summary>Enters recording mode and waits for one complete combination.</summary>
    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    public async Task StartRecordingAsync()
    {
        // CaptureAsync answers a concurrent call with Canceled immediately, which would be
        // indistinguishable from the user cancelling. Tracking that a capture is open is what tells
        // the two apart.
        if (IsRecording || !IsAvailable)
        {
            return;
        }

        using var capture = new CancellationTokenSource();
        _capture = capture;
        IsRecording = true;
        Message = "Press a key combination...";

        try
        {
            // A capture that is neither a binding nor a cancel has left its reason in Message and
            // asks for another key.
            var finished = false;
            while (!finished)
            {
                finished = await RecordOnceAsync(capture.Token).ConfigureAwait(true);
            }
        }
        finally
        {
            IsRecording = false;
            _capture = null;
        }
    }

    /// <summary>
    /// Waits for one capture and answers whether recording is over — a binding was applied, or the
    /// user cancelled. A <c>false</c> asks for another key and leaves the reason in
    /// <see cref="Message"/>.
    /// </summary>
    private async Task<bool> RecordOnceAsync(CancellationToken token)
    {
        var result = await _hotkeys.CaptureAsync(token).ConfigureAwait(true);

        if (result.Outcome == HotkeyCaptureOutcome.Cancelled)
        {
            _logger.LogDebug("Hotkey capture cancelled.");
            Message = null;
            return true;
        }

        if (result.Outcome == HotkeyCaptureOutcome.KeyNotSupported)
        {
            _logger.LogDebug("Hotkey capture saw a key this vocabulary cannot name.");
            Message = "That key cannot be used as a hotkey. Try another combination.";
            return false;
        }

        var binding = result.Binding!;

        if (IsCancelKey(binding))
        {
            // Escape is in the vocabulary, so the capture returns it as a binding rather than
            // treating it as a cancel. Reading it here is deterministic and needs no second
            // input path; Ctrl+Escape stays bindable because it carries a modifier.
            _logger.LogDebug("Hotkey capture cancelled with Escape.");
            Message = null;
            return true;
        }

        if (binding.Modifiers.Count == 0)
        {
            // A bare key is a legal capture and a terrible hotkey: it would stop working
            // everywhere else on the machine.
            Message = "Hold at least one modifier — Ctrl, Alt, Shift or Cmd — with the key.";
            return false;
        }

        Apply(binding);
        return true;
    }

    /// <summary>Abandons a capture in progress, leaving the binding as it was.</summary>
    [RelayCommand(CanExecute = nameof(IsRecording))]
    public void CancelRecording()
    {
        Cancel();
    }

    /// <summary>
    /// Ends any capture in progress. Called by Cancel, by the window hiding, and by the window being
    /// deactivated, because an open capture is a hotkey that does nothing.
    /// </summary>
    public void Cancel()
    {
        _capture?.Cancel();
    }

    private static bool IsCancelKey(HotkeyBinding binding)
    {
        return binding.Modifiers.Count == 0
               && string.Equals(binding.Key, "Escape", StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(HotkeyBinding binding)
    {
        return string.Join(" + ", binding.Modifiers.Append(binding.Key));
    }

    private bool CanStartRecording()
    {
        return IsAvailable && !IsRecording;
    }

    private void Apply(HotkeyBinding binding)
    {
        // The chord that was accepted, and nothing else about any key that was pressed.
        var described = Describe(binding);
        _logger.LogInformation("Hotkey recorded as {Hotkey}.", described);

        Binding = described;
        ConflictsWithSystemHotkey = ConflictDetector.ConflictsWithSystemHotkey(binding);
        Message = null;

        // Copied into the draft rather than assigned by reference: the capture's binding is the
        // caller's object, and the draft is replaced on every commit.
        var modifiers = binding.Modifiers.ToList();
        var key = binding.Key;

        _editor.Edit(settings =>
            settings.Hotkey = new HotkeyBinding {Modifiers = [.. modifiers], Key = key});
    }
}
