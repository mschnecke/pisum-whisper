namespace Pisum.Whisper.Core.Hotkeys;

using Pisum.Whisper.Core.Settings;

/// <summary>How a capture ended.</summary>
public enum HotkeyCaptureOutcome
{
    /// <summary>A combination was captured and can be written into settings.</summary>
    Captured,

    /// <summary>
    /// A key was pressed that this vocabulary has no name for, so the combination cannot be
    /// persisted. Reported rather than silently ignored, so the recorder can say why.
    /// </summary>
    KeyNotSupported,

    /// <summary>The capture was cancelled before a combination was pressed.</summary>
    Cancelled,
}

/// <summary>The result of a capture, in the form the settings file accepts.</summary>
public readonly record struct HotkeyCapture(HotkeyCaptureOutcome Outcome, HotkeyBinding? Binding)
{
    public static HotkeyCapture Cancelled { get; } = new(HotkeyCaptureOutcome.Cancelled, null);

    public static HotkeyCapture KeyNotSupported { get; } = new(HotkeyCaptureOutcome.KeyNotSupported, null);
}
