namespace Pisum.Whisper.Core.Settings;

/// <summary>The global hotkey that starts and stops recording.</summary>
public sealed class HotkeyBinding
{
    public List<string> Modifiers { get; set; } =
        OperatingSystem.IsMacOS() ? ["Cmd", "Shift"] : ["Ctrl", "Shift"];

    public string Key { get; set; } = "Space";
}
