namespace Pisum.Whisper.Platform.Output;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pisum.Whisper.Core.Output;

/// <summary>
/// The macOS general pasteboard through <c>NSPasteboard</c> and the Objective-C runtime.
/// </summary>
/// <remarks>
/// <para>
/// Change 1's spike used <c>pbcopy</c>/<c>pbpaste</c>, which was right for a spike and remains the
/// documented fallback if this proves troublesome. It is not right here: it costs three process
/// launches per dictation, cannot inspect what is on the pasteboard, and cannot set the concealed
/// type below.
/// </para>
/// <para>
/// <c>objc_msgSend</c> is variadic, so each call shape is imported under its own name — the runtime
/// entry point is the same function in every case.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed partial class MacOsClipboard : ISystemClipboard
{
    private const string ObjectiveCRuntime = "/usr/lib/libobjc.A.dylib";

    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    /// <summary>
    /// The convention clipboard managers honour to keep an entry out of their history. Not an Apple
    /// API — a community convention (nspasteboard.org) — which is why it is verified by hand rather
    /// than assumed to work.
    /// </summary>
    private const string ConcealedType = "org.nspasteboard.ConcealedType";

    private const int RtldNow = 2;

    /// <summary>
    /// AppKit is loaded before any class is looked up, so <c>NSPasteboard</c> is present regardless
    /// of what else this process happens to have loaded. Done once, statically, because the pointers
    /// cached below are only valid after it.
    /// </summary>
    private static readonly IntPtr AppKitHandle = LoadAppKit();

    private static readonly IntPtr PasteboardClass = GetClass("NSPasteboard");

    private static readonly IntPtr StringClass = GetClass("NSString");

    private static readonly IntPtr GeneralPasteboardSelector = RegisterSelector("generalPasteboard");

    private static readonly IntPtr ClearContentsSelector = RegisterSelector("clearContents");

    private static readonly IntPtr SetStringForTypeSelector = RegisterSelector("setString:forType:");

    private static readonly IntPtr StringForTypeSelector = RegisterSelector("stringForType:");

    private static readonly IntPtr StringWithUtf8Selector = RegisterSelector("stringWithUTF8String:");

    private static readonly IntPtr Utf8StringSelector = RegisterSelector("UTF8String");

    public string? TryGetText()
    {
        // Every object below arrives autoreleased — the pasteboard's return values as much as the
        // strings this class makes — and nothing drains a pool for us: this runs on a thread-pool
        // thread rather than inside an AppKit callback, where the run loop would drain one per
        // iteration. Without this the process leaks a little of the user's speech per dictation.
        var pool = AutoreleasePoolPush();

        try
        {
            var pasteboard = SendMessage(PasteboardClass, GeneralPasteboardSelector);
            var value = SendMessage(pasteboard, StringForTypeSelector, PasteboardTypeString());

            if (value == IntPtr.Zero)
            {
                return null;
            }

            var utf8 = SendMessage(value, Utf8StringSelector);

            // Copied into a managed string before the pop: the buffer belongs to the autoreleased
            // NSString above and does not outlive it.
            return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
        }
        finally
        {
            AutoreleasePoolPop(pool);
        }
    }

    public void SetText(string text)
    {
        var pool = AutoreleasePoolPush();

        try
        {
            var pasteboard = SendMessage(PasteboardClass, GeneralPasteboardSelector);
            SendMessageReturningLong(pasteboard, ClearContentsSelector);

            // The concealed type first: a manager watching the pasteboard reads it after the write
            // that carries the text, so the mark has to already be there.
            SendMessageSettingString(
                pasteboard, SetStringForTypeSelector, NewString(string.Empty), NewString(ConcealedType));

            if (!SendMessageSettingString(pasteboard, SetStringForTypeSelector, NewString(text), PasteboardTypeString()))
            {
                throw new InvalidOperationException("NSPasteboard refused the text.");
            }
        }
        finally
        {
            // Safe as soon as the writes above return: the pasteboard holds its own copy of what it
            // was handed, so these strings are ours to let go of.
            AutoreleasePoolPop(pool);
        }
    }

    private static IntPtr PasteboardTypeString()
    {
        // An NSString constant exported by AppKit, so the symbol holds the pointer rather than being it.
        var symbol = DlSym(AppKitHandle, "NSPasteboardTypeString");

        return symbol == IntPtr.Zero
            ? throw new InvalidOperationException("AppKit did not export NSPasteboardTypeString.")
            : Marshal.ReadIntPtr(symbol);
    }

    private static IntPtr NewString(string value) =>
        SendMessageWithUtf8(StringClass, StringWithUtf8Selector, value);

    private static IntPtr LoadAppKit()
    {
        var handle = DlOpen(AppKit, RtldNow);

        return handle == IntPtr.Zero
            ? throw new InvalidOperationException($"AppKit could not be loaded from {AppKit}.")
            : handle;
    }

    [LibraryImport("/usr/lib/libSystem.dylib", EntryPoint = "dlopen", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr DlOpen(string path, int mode);

    [LibraryImport("/usr/lib/libSystem.dylib", EntryPoint = "dlsym", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr DlSym(IntPtr handle, string symbol);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetClass(string name);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RegisterSelector(string name);

    /// <summary>
    /// The two halves of <c>@autoreleasepool</c>, which is all that keyword compiles to. Popping
    /// must happen on the pushing thread and in reverse order, which the <c>try</c>/<c>finally</c>
    /// pairs above guarantee — both callers are synchronous and never await inside the scope.
    /// </summary>
    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_autoreleasePoolPush")]
    private static partial IntPtr AutoreleasePoolPush();

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_autoreleasePoolPop")]
    private static partial void AutoreleasePoolPop(IntPtr pool);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendMessage(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static partial long SendMessageReturningLong(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr SendMessageWithUtf8(IntPtr receiver, IntPtr selector, string argument);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SendMessageSettingString(IntPtr receiver, IntPtr selector, IntPtr value, IntPtr type);
}
