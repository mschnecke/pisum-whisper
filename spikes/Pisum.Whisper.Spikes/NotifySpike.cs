using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pisum.Whisper.Spikes;

/// <summary>
/// S6 (change 11) — is <c>Shell_NotifyIcon</c> with <c>NIF_INFO</c> a usable notification transport
/// for this application on Windows?
/// </summary>
/// <remarks>
/// <para>
/// The proposal names <c>CommunityToolkit.WinUI.Notifications</c>, whose desktop half
/// (<c>ToastNotificationManagerCompat</c>) ships only under <c>lib/net5.0-windows10.0.18362</c> — so
/// a plain <c>net10.0</c> project resolves <c>lib/net5.0</c> and gets the XML builder with no way to
/// show anything. Adopting it means a <c>-windows</c> TFM, which the project has decided against.
/// <c>Shell_NotifyIcon</c> is pure P/Invoke, needs no package, no AUMID and no Start-menu shortcut,
/// and Windows 10+ renders its balloons as real toasts. Four things have to be true for that to be
/// the answer:
/// </para>
/// <list type="number">
/// <item>Q1 — a toast appears at all, from an unpackaged exe with no AUMID registered.</item>
/// <item>Q2 — a message-only window (<c>HWND_MESSAGE</c>) can own the icon, or a real top-level
/// window is required. This process has no window of its own; Avalonia owns the tray icon and
/// <c>Avalonia.Win32.TrayIconImpl</c> is internal, so the notification icon must be ours.</item>
/// <item>Q3 — <b>the blocker</b>: does a balloon fire for an icon added with <c>NIS_HIDDEN</c>? If
/// it does not, this transport costs the user a second, permanent icon in the notification area
/// beside Avalonia's, which is a visible defect and probably disqualifying.</item>
/// <item>Q4 — the result persists in the Action Center rather than only flashing past.</item>
/// </list>
/// <para>
/// There is no API that answers "did a toast appear", so the trials are observed: each one prints
/// what it did and every <c>Shell_NotifyIcon</c> return value, then asks. A <c>false</c> return
/// settles a trial on its own; a <c>true</c> return does not, which is the whole reason for the
/// prompts. Run it with Focus Assist / Do Not Disturb OFF, or every trial fails for the wrong reason.
/// </para>
/// <para>
/// Deliberately not covered, because they are production concerns rather than open questions: the
/// <c>TaskbarCreated</c> re-add after an explorer restart, and click activation. Both need a real
/// window procedure; these trials use the system <c>STATIC</c> class, which needs none.
/// </para>
/// </remarks>
internal static partial class NotifySpike
{
    private const uint NimAdd = 0x0;

    private const uint NimModify = 0x1;

    private const uint NimDelete = 0x2;

    private const uint NimSetVersion = 0x4;

    private const uint NifMessage = 0x01;

    private const uint NifIcon = 0x02;

    private const uint NifTip = 0x04;

    private const uint NifState = 0x08;

    private const uint NifInfo = 0x10;

    private const uint NifShowTip = 0x80;

    private const uint NisHidden = 0x1;

    private const uint NiifInfo = 0x1;

    private const uint NotifyIconVersion4 = 4;

    /// <summary>The parent that makes a window message-only: invisible, never activated, no z-order.</summary>
    private static readonly IntPtr HwndMessage = new(-3);

    private const int IdiApplication = 32512;

    public static async Task<int> RunAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("S6 is a Windows question. Nothing to run here.");
            return 0;
        }

        Console.WriteLine("S6 — Shell_NotifyIcon + NIF_INFO as a notification transport");
        Console.WriteLine(new string('-', 72));
        Console.WriteLine($"exe          : {Environment.ProcessPath}");
        Console.WriteLine($"AUMID        : {CurrentAumid() ?? "<none set — which is the point>"}");
        Console.WriteLine($"NOTIFYICONDATAW size: {NotifyIconDataSize()} bytes (expected 976 on x64)");
        Console.WriteLine();
        Console.WriteLine("Turn Focus Assist / Do Not Disturb OFF before answering, or every trial");
        Console.WriteLine("fails for a reason that is not the one being measured.");
        Console.WriteLine();

        var trials = new List<Trial>();

        // T1 — the baseline. A visible icon on a message-only window answers Q1 and Q2 together:
        // if this shows nothing, the transport is dead and T2 and T3 measure noise.
        trials.Add(await RunTrialAsync(
            "T1",
            "message-only window (HWND_MESSAGE), icon VISIBLE",
            messageOnly: true,
            hidden: false,
            uid: 1,
            "Pisum Whisper S6 — T1",
            "Baseline. A visible icon on a message-only window."));

        // T2 — the blocker. Same window, same everything, NIS_HIDDEN set at NIM_ADD time.
        trials.Add(await RunTrialAsync(
            "T2",
            "message-only window (HWND_MESSAGE), icon NIS_HIDDEN",
            messageOnly: true,
            hidden: true,
            uid: 2,
            "Pisum Whisper S6 — T2",
            "The blocker. A hidden icon on a message-only window."));

        // T3 — the fallback shape if T2 fails only because the window is message-only: an ordinary
        // top-level window that is created and never shown is still no icon and no taskbar button.
        trials.Add(await RunTrialAsync(
            "T3",
            "top-level window, never shown, icon NIS_HIDDEN",
            messageOnly: false,
            hidden: true,
            uid: 3,
            "Pisum Whisper S6 — T3",
            "The fallback. A hidden icon on an unshown top-level window."));

        var actionCentre = Ask(
            "Q4: open the Action Center (Win+N). Are any of the toasts above still listed there?");

        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine("S6 RESULTS");
        Console.WriteLine(new string('=', 72));
        foreach (var trial in trials)
        {
            Console.WriteLine($"  {trial.Name}  {trial.Description}");
            Console.WriteLine($"        add={Show(trial.Added)} version={Show(trial.Versioned)} " +
                              $"info={Show(trial.Info)}  toast seen: {Show(trial.Seen)}");
        }

        // Either hidden-icon trial passing settles Q3; it takes both failing to settle it the other
        // way, and an unanswered pair leaves it open rather than failed. Folding null into false
        // here is what made a redirected run print a confident FAIL it had measured nothing to earn.
        var hiddenWorks = Any(trials[1].Seen, trials[2].Seen);

        Console.WriteLine();
        Console.WriteLine($"  Q1 toast at all, no AUMID   : {Verdict(trials[0].Seen)}");
        Console.WriteLine($"  Q2 message-only window ok   : {Verdict(trials[0].Seen)}");
        Console.WriteLine($"  Q3 fires for a HIDDEN icon  : {Verdict(hiddenWorks)}");
        Console.WriteLine($"  Q4 persists in Action Center: {Verdict(actionCentre)}");
        Console.WriteLine();

        // The transport is only worth proposing if a hidden icon works: a permanent second icon in
        // the notification area beside Avalonia's own is a defect the user sees every day.
        var usable = And(trials[0].Seen, hiddenWorks);
        Console.WriteLine($"S6 VERDICT: {usable switch
        {
            true => "PASS - Shell_NotifyIcon is a viable identity-free transport with no visible icon",
            false => "FAIL - see the rows above",
            null => "UNANSWERED - the API accepted every call, but nobody watched the screen. "
                    + "Run it from a real console.",
        }}");

        return usable == true ? 0 : 1;
    }

    private static async Task<Trial> RunTrialAsync(string name,
                                                   string description,
                                                   bool messageOnly,
                                                   bool hidden,
                                                   uint uid,
                                                   string title,
                                                   string body)
    {
        Console.WriteLine(new string('-', 72));
        Console.WriteLine($"{name}: {description}");

        var window = CreateWindow(messageOnly);
        Console.WriteLine($"  window       : 0x{window:X} ({(messageOnly ? "HWND_MESSAGE child" : "top-level, never shown")})");

        if (window == IntPtr.Zero)
        {
            Console.WriteLine($"  CreateWindowExW failed, error {Marshal.GetLastWin32Error()}");
            return new Trial(name, description, false, false, false, false);
        }

        var trial = new Trial(name, description, false, false, false, null);

        try
        {
            var icon = LoadIconW(IntPtr.Zero, IdiApplication);

            var added = Add(window, uid, icon, hidden);
            Console.WriteLine($"  NIM_ADD      : {Show(added)}{Error(added)}");

            var versioned = SetVersion(window, uid);
            Console.WriteLine($"  NIM_SETVERSION v4: {Show(versioned)}{Error(versioned)}");

            // Pumped rather than slept through: Shell_NotifyIcon talks to the taskbar with
            // SendMessage, and a thread that never pumps is a thread the shell can block on.
            await PumpAsync(TimeSpan.FromMilliseconds(400));

            var info = ShowBalloon(window, uid, hidden, title, body);
            Console.WriteLine($"  NIF_INFO     : {Show(info)}{Error(info)}");

            await PumpAsync(TimeSpan.FromSeconds(2));

            var seen = Ask($"  {name}: did a toast titled \"{title}\" appear?");

            trial = trial with {Added = added, Versioned = versioned, Info = info, Seen = seen};
            return trial;
        }
        finally
        {
            // After the answer, never before: on the classic balloon path NIM_DELETE takes the
            // balloon down with the icon, which would race the thing being observed.
            Delete(window, uid);
            DestroyWindow(window);
            await PumpAsync(TimeSpan.FromMilliseconds(300));
        }
    }

    private static unsafe bool Add(IntPtr window, uint uid, IntPtr icon, bool hidden)
    {
        var data = NewData(window, uid);
        data.uFlags = NifMessage | NifIcon | NifTip | (hidden ? NifState : NifShowTip);
        data.uCallbackMessage = 0x0400 + 1;
        data.hIcon = icon;
        Write(data.szTip, 128, "Pisum Whisper S6");

        if (hidden)
        {
            data.dwState = NisHidden;
            data.dwStateMask = NisHidden;
        }

        return ShellNotifyIconW(NimAdd, &data);
    }

    private static unsafe bool SetVersion(IntPtr window, uint uid)
    {
        var data = NewData(window, uid);
        data.uVersionOrTimeout = NotifyIconVersion4;
        return ShellNotifyIconW(NimSetVersion, &data);
    }

    private static unsafe bool ShowBalloon(IntPtr window, uint uid, bool hidden, string title, string body)
    {
        var data = NewData(window, uid);
        data.uFlags = NifInfo;
        data.dwInfoFlags = NiifInfo;
        Write(data.szInfoTitle, 64, title);
        Write(data.szInfo, 256, body);

        // Re-asserted with the balloon, not only at NIM_ADD: a MODIFY that omits NIF_STATE leaves the
        // state alone in principle, and this trial must not accidentally unhide the icon it is
        // measuring.
        if (hidden)
        {
            data.uFlags |= NifState;
            data.dwState = NisHidden;
            data.dwStateMask = NisHidden;
        }

        return ShellNotifyIconW(NimModify, &data);
    }

    private static unsafe void Delete(IntPtr window, uint uid)
    {
        var data = NewData(window, uid);
        ShellNotifyIconW(NimDelete, &data);
    }

    private static NotifyIconData NewData(IntPtr window, uint uid)
    {
        return new NotifyIconData
        {
            cbSize = (uint)NotifyIconDataSize(),
            hWnd = window,
            uID = uid,
        };
    }

    private static unsafe int NotifyIconDataSize()
    {
        return sizeof(NotifyIconData);
    }

    private static IntPtr CreateWindow(bool messageOnly)
    {
        // The system STATIC class rather than a registered one of our own: these trials never
        // receive a message, so a window procedure would be ceremony. Production needs one, for
        // TaskbarCreated after an explorer restart.
        return CreateWindowExW(
            0,
            "STATIC",
            "Pisum Whisper S6",
            0,
            0,
            0,
            0,
            0,
            messageOnly ? HwndMessage : IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private static async Task PumpAsync(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until)
        {
            while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, 0x0001))
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }

            await Task.Delay(20);
        }
    }

    private static bool? Ask(string question)
    {
        if (Console.IsInputRedirected)
        {
            Console.WriteLine($"{question}  [no console to answer from — unanswered]");
            return null;
        }

        Console.Write($"{question} [y/n] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer is "y" or "yes" ? true : answer is "n" or "no" ? false : null;
    }

    private static string? CurrentAumid()
    {
        if (GetCurrentProcessExplicitAppUserModelID(out var id) != 0 || id == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(id);
        }
        finally
        {
            Marshal.FreeCoTaskMem(id);
        }
    }

    private static unsafe void Write(char* destination, int capacity, string value)
    {
        var length = Math.Min(value.Length, capacity - 1);
        for (var i = 0; i < length; i++)
        {
            destination[i] = value[i];
        }

        destination[length] = '\0';
    }

    private static string Show(bool? value)
    {
        return value switch {true => "yes", false => "NO", _ => "?"};
    }

    private static string Error(bool succeeded)
    {
        return succeeded ? string.Empty : $" (error {Marshal.GetLastWin32Error()})";
    }

    private static string Verdict(bool? value)
    {
        return value switch {true => "PASS", false => "FAIL", _ => "unanswered"};
    }

    /// <summary>Three-valued or: one <c>true</c> wins, one unanswered leaves the answer open.</summary>
    private static bool? Any(bool? left, bool? right)
    {
        return left == true || right == true ? true
            : left is null || right is null ? null
            : false;
    }

    /// <summary>Three-valued and: one <c>false</c> wins, one unanswered leaves the answer open.</summary>
    private static bool? And(bool? left, bool? right)
    {
        return left == false || right == false ? false
            : left is null || right is null ? null
            : true;
    }

    private sealed record Trial(string Name,
                                string Description,
                                bool? Added,
                                bool? Versioned,
                                bool? Info,
                                bool? Seen);

    /// <summary>
    /// <c>NOTIFYICONDATAW</c>, the full v4 layout. Blittable with inline buffers because
    /// <c>LibraryImport</c> will not marshal <c>ByValTStr</c>; the trailing <c>hBalloonIcon</c> is
    /// what makes <c>cbSize</c> 976 on x64, and a short <c>cbSize</c> is how the shell is told to
    /// interpret the struct as an older version.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NotifyIconData
    {
        public uint cbSize;

        public IntPtr hWnd;

        public uint uID;

        public uint uFlags;

        public uint uCallbackMessage;

        public IntPtr hIcon;

        public fixed char szTip[128];

        public uint dwState;

        public uint dwStateMask;

        public fixed char szInfo[256];

        public uint uVersionOrTimeout;

        public fixed char szInfoTitle[64];

        public uint dwInfoFlags;

        public Guid guidItem;

        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr hwnd;

        public uint message;

        public IntPtr wParam;

        public IntPtr lParam;

        public uint time;

        public int x;

        public int y;
    }

    // The export carries an underscore — Shell_NotifyIconW, not ShellNotifyIconW — so the entry
    // point has to be named explicitly rather than inferred from the method.
    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ShellNotifyIconW(uint message, NotifyIconData* data);

    [LibraryImport("shell32.dll")]
    private static partial int GetCurrentProcessExplicitAppUserModelID(out IntPtr appId);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(uint exStyle,
                                                  string className,
                                                  string windowName,
                                                  uint style,
                                                  int x,
                                                  int y,
                                                  int width,
                                                  int height,
                                                  IntPtr parent,
                                                  IntPtr menu,
                                                  IntPtr instance,
                                                  IntPtr param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr window);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr LoadIconW(IntPtr instance, int name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Message message,
                                             IntPtr window,
                                             uint filterMin,
                                             uint filterMax,
                                             uint removeMessage);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref Message message);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DispatchMessageW(ref Message message);
}
