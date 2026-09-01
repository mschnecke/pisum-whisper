namespace Pisum.Whisper.App.Notifications;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

/// <summary>
/// One notification, drawn by this application rather than by the operating system.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>ShowActivated = false</c> is the whole reason this transport is usable.</b> This
/// application pastes at the cursor in whatever the user is typing in, so a notification that
/// activates does not merely look wrong — it takes away the target the next dictation would be
/// delivered to. Win32 turns the flag into <c>SW_SHOWNOACTIVATE</c> and macOS into a bare
/// <c>orderFront:</c> with no <c>ActivateApplication</c>; change 11's S7 spike measured that the
/// foreground application is unchanged on win-x64. <see cref="Window.Topmost"/> is separate: it
/// decides z-order, not focus.
/// </para>
/// <para>
/// There is deliberately <b>no click handling</b>. Dismissal is by timer alone, in
/// <see cref="ToastPresenter"/>. On macOS clicking a non-key window would make it key and activate
/// an accessory application, spending exactly the focus the paragraph above protects.
/// </para>
/// </remarks>
public sealed partial class ToastWindow : Window
{
    private const double ToastWidth = 360;

    private const double ToastHeight = 96;

    /// <summary>The gap between two stacked notifications, so neither covers the other.</summary>
    private const double Gap = 8;

    /// <summary>The distance from the working area's corner, on both axes.</summary>
    private const double EdgeMargin = 16;

    /// <summary>
    /// The parameterless constructor Avalonia's XAML compiler requires. The window is always built
    /// through the overload below; this one exists so the compiled loader has something to call.
    /// </summary>
    public ToastWindow()
    {
        InitializeComponent();
    }

    public ToastWindow(string title, string message)
        : this()
    {
        // The window's own Title is what an alt-tab entry or a window list would show, so it names
        // the application rather than repeating the notification.
        Title = "Pisum Whisper";

        // Looked up rather than reached through generated name fields: every code-behind in this
        // project writes its own InitializeComponent, which takes precedence over the generated
        // overload and leaves those fields unassigned.
        this.GetControl<TextBlock>("TitleText").Text = title;
        this.GetControl<TextBlock>("MessageText").Text = message;
    }

    /// <summary>
    /// Where a notification in <paramref name="slot"/> goes, given a working area and its scaling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The corner differs by platform and this deliberately does not pretend otherwise.</b>
    /// Windows notifications rise from the bottom right above the taskbar; macOS ones descend from
    /// the top right below the menu bar. <see cref="Screen.WorkingArea"/> is what keeps both clear
    /// of the taskbar, the Dock and the menu bar.
    /// </para>
    /// <para>
    /// <paramref name="scaling"/> is read because <see cref="WindowBase.Position"/> is in physical
    /// pixels while <see cref="Layout.Layoutable.Width"/> and <see cref="Layout.Layoutable.Height"/>
    /// are not. S7 measured 540 x 144 physical from 360 x 96 logical at 1.5 scaling, and
    /// <c>GetWindowRect</c> returned exactly the requested rectangle.
    /// </para>
    /// <para>
    /// Static and taking its geometry as arguments so that the arithmetic is assertable against a
    /// stubbed working area: a headless test has no screen to read one from.
    /// </para>
    /// </remarks>
    public static PixelRect PositionFor(PixelRect workingArea, double scaling, int slot)
    {
        var width = (int) (ToastWidth * scaling);
        var height = (int) (ToastHeight * scaling);
        var margin = (int) (EdgeMargin * scaling);
        var step = height + (int) (Gap * scaling);

        var x = workingArea.X + workingArea.Width - width - margin;
        var y = OperatingSystem.IsMacOS()
            ? workingArea.Y + margin + (slot * step)
            : workingArea.Y + workingArea.Height - height - margin - (slot * step);

        return new PixelRect(x, y, width, height);
    }

    /// <summary>
    /// Places this notification in the platform's notification corner, <paramref name="slot"/>
    /// places along the stack. A machine reporting no screen at all is left where it was rather
    /// than positioned at the origin.
    /// </summary>
    public void PlaceInCorner(int slot)
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();

        if (screen is null)
        {
            return;
        }

        Position = PositionFor(screen.WorkingArea, screen.Scaling, slot).Position;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
