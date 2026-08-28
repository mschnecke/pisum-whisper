namespace Pisum.Whisper.Core.Settings;

/// <summary>
/// Represents a hotkey binding that defines a combination of key modifiers
/// and a main key used to trigger an action.
/// </summary>
public sealed class HotkeyBinding
{
    /// <summary>
    /// The list of modifiers used to form a hotkey combination.
    /// On macOS, the default modifiers are "Cmd" and "Shift".
    /// On other operating systems, the default modifiers are "Ctrl" and "Shift".
    /// </summary>
    public List<string> Modifiers { get; set; } =
        OperatingSystem.IsMacOS() ? ["Cmd", "Shift"] : ["Ctrl", "Shift"];

    /// <summary>
    /// The key that triggers a hotkey binding.
    /// This property defines the specific key assigned to a hotkey,
    /// such as "Space" or "F9", to initiate a predefined action.
    /// By default, the key is set to "Space".
    /// </summary>
    public string Key { get; set; } = "Space";
}
