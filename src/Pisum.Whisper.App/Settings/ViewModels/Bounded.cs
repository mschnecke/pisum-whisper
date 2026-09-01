namespace Pisum.Whisper.App.Settings.ViewModels;

using System.Globalization;

/// <summary>
/// The clamp every numeric field in the window shares.
/// </summary>
/// <remarks>
/// These values reach a recording watchdog, a log rotation size, and a retention sweep, where a zero
/// or a negative one is not a preference but a defect the user typed in by accident. It lives in the
/// view-model layer, so it is tested without a UI, and it mirrors the reference's <c>parseInt</c>
/// guards. What is clamped is the value written to settings, not the text in the box: rewriting the
/// text as it is typed would turn "3600" into "10" at the first keystroke.
/// </remarks>
internal static class Bounded
{
    /// <summary>
    /// Confines <paramref name="text"/> to <paramref name="minimum"/>..<paramref name="maximum"/>,
    /// falling back to the minimum when it is empty or not a number.
    /// </summary>
    public static int Clamp(string? text, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return minimum;
        }

        return Math.Clamp(value, minimum, maximum);
    }
}
