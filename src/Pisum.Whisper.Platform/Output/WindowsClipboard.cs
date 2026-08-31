namespace Pisum.Whisper.Platform.Output;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pisum.Whisper.Core.Output;

/// <summary>
/// The Windows clipboard through the plain Win32 API.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership is why this is the right route</b>, beyond Avalonia's being unavailable to a
/// window-less process: <c>SetClipboardData</c> hands the data to the clipboard, so it survives this
/// process exiting. That matters precisely in the degraded case, where the transcript sits on the
/// clipboard waiting for a user who may quit the application before pasting it.
/// </para>
/// <para>
/// Deliberately branchless beyond its retries and its null checks — every decision in this
/// capability lives in <see cref="Pisum.Whisper.Core.Output.TextOutput"/>, which is the half that
/// can be unit-tested.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsClipboard : ISystemClipboard
{
    private const uint UnicodeText = 13;

    private const uint GlobalMoveable = 0x0002;

    /// <summary>
    /// Another process holding the clipboard is routine on a normal desktop rather than
    /// exceptional, so a first failure to open is retried rather than reported.
    /// </summary>
    private const int OpenAttempts = 10;

    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Written as a <c>DWORD</c> 0 alongside the text to keep it out of the Win+V history and out of
    /// the cloud clipboard tied to the user's Microsoft account.
    /// </summary>
    private const string HistoryFormatName = "CanIncludeInClipboardHistory";

    /// <summary>Written alongside the text to keep clipboard monitors from processing it.</summary>
    private const string MonitorFormatName = "ExcludeClipboardContentFromMonitorProcessing";

    public string? TryGetText()
    {
        Open();

        try
        {
            if (!IsClipboardFormatAvailable(UnicodeText))
            {
                return null;
            }

            var handle = GetClipboardData(UnicodeText);

            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var pointer = GlobalLock(handle);

            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void SetText(string text)
    {
        Open();

        try
        {
            if (!EmptyClipboard())
            {
                throw Failure("The clipboard could not be emptied");
            }

            // In the same session as the text below, so the marks and the text cannot disagree —
            // a transcript on the clipboard without them is the user's speech in Win+V.
            Exclude(HistoryFormatName);
            Exclude(MonitorFormatName);

            var handle = Copy(text);

            if (SetClipboardData(UnicodeText, handle) == IntPtr.Zero)
            {
                GlobalFree(handle);
                throw Failure("The text could not be placed on the clipboard");
            }

            // The handle is deliberately not freed on success: SetClipboardData transfers ownership
            // to the system, and freeing it here would leave the clipboard pointing at released
            // memory.
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void Open()
    {
        for (var attempt = 1; ; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return;
            }

            if (attempt == OpenAttempts)
            {
                throw Failure($"The clipboard could not be opened after {OpenAttempts} attempts");
            }

            Thread.Sleep(OpenRetryDelay);
        }
    }

    /// <summary>
    /// Registers <paramref name="formatName"/> and puts a <c>DWORD</c> 0 on the clipboard under it.
    /// Both exclusion formats are read by their presence; the zero is what
    /// <c>CanIncludeInClipboardHistory</c> is documented to carry.
    /// </summary>
    private static void Exclude(string formatName)
    {
        var format = RegisterClipboardFormat(formatName);

        if (format == 0)
        {
            throw Failure($"The clipboard format '{formatName}' could not be registered");
        }

        var handle = GlobalAlloc(GlobalMoveable, sizeof(uint));

        if (handle == IntPtr.Zero)
        {
            throw Failure($"Memory for the clipboard format '{formatName}' could not be allocated");
        }

        var pointer = GlobalLock(handle);

        if (pointer == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw Failure($"Memory for the clipboard format '{formatName}' could not be locked");
        }

        Marshal.WriteInt32(pointer, 0);
        GlobalUnlock(handle);

        if (SetClipboardData(format, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw Failure($"The clipboard format '{formatName}' could not be set");
        }
    }

    /// <summary>Copies <paramref name="text"/> into moveable global memory as UTF-16.</summary>
    private static IntPtr Copy(string text)
    {
        var bytes = (nuint)((text.Length + 1) * sizeof(char));
        var handle = GlobalAlloc(GlobalMoveable, bytes);

        if (handle == IntPtr.Zero)
        {
            throw Failure("Memory for the clipboard text could not be allocated");
        }

        var pointer = GlobalLock(handle);

        if (pointer == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw Failure("Memory for the clipboard text could not be locked");
        }

        try
        {
            // Null-terminated, because CF_UNICODETEXT is read as a C string rather than by length.
            var characters = new char[text.Length + 1];
            text.CopyTo(characters);
            characters[text.Length] = '\0';

            Marshal.Copy(characters, 0, pointer, characters.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        return handle;
    }

    private static Win32Exception Failure(string what) => new(Marshal.GetLastPInvokeError(), $"{what}.");

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr newOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetClipboardData(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint format, IntPtr data);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string format);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalFree(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr handle);
}
