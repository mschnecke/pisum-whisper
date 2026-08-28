namespace Pisum.Whisper.Core.Hotkeys;

/// <summary>The two edges of the binding this capability reports.</summary>
public enum HotkeyEdge
{
    /// <summary>The binding became fully held.</summary>
    Pressed,

    /// <summary>The binding stopped being fully held.</summary>
    Released,
}
