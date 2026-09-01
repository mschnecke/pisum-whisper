namespace Pisum.Whisper.App.Tests.Views;

using System.Reflection;
using Avalonia.Controls;

/// <summary>
/// The two window callbacks the operating system makes and a test cannot, reached by reflection.
/// </summary>
/// <remarks>
/// Both are what Avalonia's platform layer itself calls, so a test using them exercises the window's
/// real handlers rather than a re-creation of them. There is no public route to either: the close
/// reason only reaches <c>Closing</c> through <c>Window.HandleClosing</c>, and <c>Deactivated</c> is
/// raised only by <c>WindowBase.HandleDeactivated</c>.
/// </remarks>
internal static class WindowInternals
{
    private static readonly MethodInfo HandleClosingMethod =
        typeof(Window).GetMethod("HandleClosing", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Window.HandleClosing is gone; the close-reason tests need it.");

    private static readonly MethodInfo HandleDeactivatedMethod =
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WindowBase.HandleDeactivated is gone; the deactivation test needs it.");

    /// <summary>Runs the window's <c>Closing</c> handlers for <paramref name="reason"/>, reporting whether the close was cancelled.</summary>
    public static bool Close(Window window, WindowCloseReason reason)
    {
        return (bool) HandleClosingMethod.Invoke(window, [reason])!;
    }

    /// <summary>Raises <c>Deactivated</c>, as the platform does when the user clicks away.</summary>
    public static void Deactivate(WindowBase window)
    {
        HandleDeactivatedMethod.Invoke(window, []);
    }
}
