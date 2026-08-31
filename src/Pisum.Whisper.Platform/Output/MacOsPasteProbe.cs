namespace Pisum.Whisper.Platform.Output;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pisum.Whisper.Core.Output;

/// <summary>
/// Whether this process may post events at all on macOS, which is the same Accessibility check
/// libuiohook makes internally.
/// </summary>
/// <remarks>
/// Definitive rather than heuristic, unlike its Windows counterpart: without the grant every
/// injected event is discarded, and <c>UioHookResult</c> still reports Success because it reports
/// what was posted rather than what was accepted.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed partial class MacOsPasteProbe : IPasteProbe
{
    public bool CanPaste() => AXIsProcessTrusted();

    [LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool AXIsProcessTrusted();
}
