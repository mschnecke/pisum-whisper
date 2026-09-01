namespace Pisum.Whisper.App.Tests.Notifications;

using Avalonia;
using Avalonia.Headless.XUnit;
using Pisum.Whisper.App.Notifications;
using Shouldly;

/// <summary>
/// Task 2.1 — the notification window: it shows, it does not take focus, and two of them stack
/// inside the working area rather than on top of each other.
/// </summary>
/// <remarks>
/// The working area is stubbed because a headless platform has no screen to read one from, and
/// because the arithmetic is the part worth asserting: <c>Position</c> is in physical pixels while
/// <c>Width</c> and <c>Height</c> are not, so the scaling is the thing that goes wrong.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class ToastWindowTests
{
    /// <summary>A 1920 x 1080 screen with a 72-pixel taskbar excluded, at 1.5 scaling — the S7 machine's shape.</summary>
    private static readonly PixelRect WorkingArea = new(0, 0, 1920, 1008);

    private const double Scaling = 1.5;

    [AvaloniaFact]
    public void TheWindowShows()
    {
        var toast = new ToastWindow("Authentication Error", "The configured key was rejected.");

        toast.Show();

        toast.IsVisible.ShouldBeTrue();

        toast.Close();
    }

    /// <summary>
    /// Headless Avalonia has no meaningful notion of focus, so nothing here can test the
    /// <em>behaviour</em>. <c>ShowActivated</c> is the mechanism, S7 measured that Avalonia honours
    /// it, and without this assertion deleting that one line would fail no test at all — while the
    /// requirement it carries is the one that breaks the product rather than annoying the user.
    /// </summary>
    [AvaloniaFact]
    public void TheWindowNeverActivates_AndStaysOutOfTheTaskbar()
    {
        var toast = new ToastWindow("Recording Error", "No input device found.");

        toast.ShowActivated.ShouldBeFalse();
        toast.Topmost.ShouldBeTrue();
        toast.ShowInTaskbar.ShouldBeFalse();
        toast.CanResize.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void BothSlotsLandInsideTheWorkingArea()
    {
        var first = ToastWindow.PositionFor(WorkingArea, Scaling, 0);
        var second = ToastWindow.PositionFor(WorkingArea, Scaling, 1);

        WorkingArea.Contains(first).ShouldBeTrue($"slot 0 at {first} left the working area {WorkingArea}");
        WorkingArea.Contains(second).ShouldBeTrue($"slot 1 at {second} left the working area {WorkingArea}");
    }

    [AvaloniaFact]
    public void TwoNotificationsDoNotCoverOneAnother()
    {
        var first = ToastWindow.PositionFor(WorkingArea, Scaling, 0);
        var second = ToastWindow.PositionFor(WorkingArea, Scaling, 1);

        first.Intersects(second).ShouldBeFalse($"{first} and {second} overlap");
    }

    /// <summary>
    /// The scaling is applied to the size as well as to the offsets: 360 x 96 logical is 540 x 144
    /// physical at 1.5, which is what S7's <c>GetWindowRect</c> returned.
    /// </summary>
    [AvaloniaFact]
    public void TheRectangleIsScaledToPhysicalPixels()
    {
        var slot = ToastWindow.PositionFor(WorkingArea, Scaling, 0);

        slot.Width.ShouldBe(540);
        slot.Height.ShouldBe(144);
    }
}
