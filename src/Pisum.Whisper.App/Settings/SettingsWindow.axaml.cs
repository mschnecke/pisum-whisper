namespace Pisum.Whisper.App.Settings;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Pisum.Whisper.App.Settings.ViewModels;

/// <summary>
/// The settings window: six tabs, no OK, no Cancel, no Apply.
/// </summary>
/// <remarks>
/// <para>
/// Closing it <b>hides</b> it, and the condition on that matters. Only
/// <see cref="WindowCloseReason.WindowClosing"/> — the user clicking the close button — is cancelled;
/// <see cref="WindowCloseReason.ApplicationShutdown"/> and <see cref="WindowCloseReason.OSShutdown"/>
/// are let through, or Quit could not close an open window and the process would hang on a window
/// refusing to go.
/// </para>
/// <para>
/// Hiding also ends any hotkey capture and flushes the editor, which are the two things that must not
/// outlive a visit: an open capture is a hotkey that does nothing, and an unflushed draft is an edit
/// the user believes they made.
/// </para>
/// </remarks>
public sealed partial class SettingsWindow : Window
{
    /// <summary>
    /// The parameterless constructor Avalonia's XAML compiler requires. The window is always built
    /// through the overload below; this one exists so the compiled loader has something to call.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();

        Closing += OnClosing;

        // Declared on WindowBase rather than on Window, and undocumented. An open capture suspends
        // hotkey matching process-wide, so a user who clicks Change and then switches to another
        // application would otherwise have no working hotkey for the rest of the session, with
        // nothing saying so.
        Deactivated += OnDeactivated;
    }

    public SettingsWindow(SettingsWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private SettingsWindowViewModel? ViewModel => DataContext as SettingsWindowViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        ViewModel?.Hotkey.Cancel();

        if (e.CloseReason != WindowCloseReason.WindowClosing)
        {
            // The application is going away. Let the window close and let App.OnExit's flush be the
            // one that runs, rather than starting a second one here on the way out.
            return;
        }

        e.Cancel = true;

        // Started before the window goes, and not awaited: a Closing handler cannot be asynchronous.
        // The flush commits a draft already in memory, and it is what bounds the debounce's worst
        // case to a killed process rather than an ordinary close.
        _ = ViewModel?.Editor.FlushAsync();
        Hide();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        ViewModel?.Hotkey.Cancel();
    }
}
