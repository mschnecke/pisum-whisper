using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Pisum.Whisper.Spikes;

/// <summary>
/// S7 (change 11) — can this application draw its own notification instead of asking the operating
/// system for one?
/// </summary>
/// <remarks>
/// <para>
/// The alternative transports all cost something structural: the toast package needs a
/// <c>-windows</c> TFM the project has decided against, and <c>Shell_NotifyIcon</c> (S6) needs a
/// second notification icon and reaches no notification platform. A window this process draws itself
/// needs no package, no AUMID, no <c>osascript</c>, and is one implementation for both targets
/// rather than a Windows/macOS pair — provided four things hold:
/// </para>
/// <list type="number">
/// <item>Q1 — <b>the blocker</b>: it must not take focus. This application pastes at the cursor in
/// whatever the user is typing in, so a notification that activates is not a cosmetic problem, it
/// breaks the product. Avalonia's two backends both branch on <c>ShowActivated</c>
/// (<c>SW_SHOWNOACTIVATE</c> on Win32, a bare <c>orderFront:</c> with no <c>ActivateApplication</c>
/// on macOS), and this measures whether that holds end to end.</item>
/// <item>Q2 — it lands in the working area, so it is clear of the taskbar on Windows and of the Dock
/// and menu bar on macOS, at the right size once DPI scaling is applied.</item>
/// <item>Q3 — two of them stack rather than land on top of each other.</item>
/// <item>Q4 — it draws above other applications' windows.</item>
/// </list>
/// <para>
/// <b>Q1 is measured rather than asked.</b> The foreground application is read before and after each
/// window is shown — <c>GetForegroundWindow</c> on Windows, <c>System Events</c> on macOS — so this
/// spike reaches a verdict with nobody watching the screen, which S6 cannot do. Q2 to Q4 still need
/// eyes, and are asked only when there is a console to answer from.
/// </para>
/// <para>
/// Deliberately unstyled beyond explicit brushes: no <c>FluentTheme</c>, so what appears is what this
/// file says and not what a theme contributes. That is also the honest shape of the option — choosing
/// it means owning the appearance, the stacking and the dismissal that the operating system would
/// otherwise supply.
/// </para>
/// </remarks>
internal static class ToastSpike
{
    public static Task<int> RunAsync()
    {
        var exit = AppBuilder.Configure<ToastApp>()
            .UsePlatformDetect()

            // Matches Program.BuildAvaloniaApp: the accessory activation policy is part of what is
            // being measured, because an app with no Dock icon activates differently.
            .With(new MacOSPlatformOptions {ShowInDock = false})
            .StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);

        return Task.FromResult(exit);
    }
}

internal sealed class ToastApp : Application
{
    private static readonly bool IsMacOS = OperatingSystem.IsMacOS();

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        // Off the UI thread: the sequence sleeps between steps and ends at a Console.ReadLine, and
        // a dispatcher blocked on either would stop the windows it is measuring from rendering.
        _ = Task.Run(async () =>
        {
            var code = await RunSequenceAsync();
            Dispatcher.UIThread.Post(() => desktop.Shutdown(code));
        });

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task<int> RunSequenceAsync()
    {
        Console.WriteLine("S7 — an Avalonia-drawn toast as the notification transport");
        Console.WriteLine(new string('-', 72));

        var before = Foreground();
        Console.WriteLine($"platform          : {(IsMacOS ? "macOS" : "Windows")}");
        Console.WriteLine($"foreground, before: {before}");
        Console.WriteLine($"this process      : {Process.GetCurrentProcess().ProcessName} (pid {Environment.ProcessId})");
        Console.WriteLine();

        var toasts = new List<Toast>();
        var samples = new List<string>();
        var probes = new List<Probe>();
        var workingArea = default(PixelRect);

        try
        {
            for (var index = 0; index < 2; index++)
            {
                var slot = index;
                var toast = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var created = new Toast(
                        $"Transcription Error #{slot + 1}",
                        "The API key was rejected. This one is drawn by the application, not by the "
                        + "operating system.");

                    created.PlaceInCorner(slot);
                    created.Show();
                    return created;
                });

                toasts.Add(toast);

                if (slot == 0)
                {
                    workingArea = await Dispatcher.UIThread.InvokeAsync(toast.ReportGeometry);
                }

                await Task.Delay(1200);

                var sample = Foreground();
                samples.Add(sample);
                Console.WriteLine($"foreground, after toast {slot + 1}: {sample}");
            }

            // Probed while both are still up, and before anything is closed. This is what stops the
            // measured Q1 from being a false pass: a window that never rendered also never takes
            // focus, so "focus did not move" only means something once the window is known to be on
            // screen.
            probes = await Dispatcher.UIThread.InvokeAsync(
                () => toasts.Select(Probe.Of).ToList());

            Console.WriteLine();
            foreach (var probe in probes)
            {
                Console.WriteLine($"  probe: {probe}");
            }

            Console.WriteLine();
            Console.WriteLine("both toasts are up — look at them now (5 s)");
            await Task.Delay(5000);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var toast in toasts)
                {
                    toast.Close();
                }
            });
        }

        await Task.Delay(400);

        // The measured half. Focus is unchanged only if every sample matches the baseline; a sample
        // naming this process is the specific failure the option would be rejected for.
        var kept = samples.All(sample => sample == before);
        var stolen = samples.Any(IsThisProcess);

        // On Windows the shell answers Q2 to Q4 itself, so the spike runs unattended there. macOS has
        // no equally cheap equivalent of WindowFromPoint through this codebase, so its half stays
        // observational — which is fine, because that half is re-run on hardware rather than here.
        var rendered = Measured(probes, probe => probe.Visible);
        var placement = Measured(probes, probe => workingArea.Contains(probe.Rect))
                        ?? Ask("Q2: did both toasts sit clear of the taskbar / Dock and menu bar?");
        var stacked = probes.Count == 2
            ? !probes[0].Rect.Intersects(probes[1].Rect)
            : Ask("Q3: did the two stack, rather than land on top of each other?");
        var above = Measured(probes, probe => probe.OnTopAtItsCentre)
                    ?? Ask("Q4: did they draw above the other windows on screen?");

        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine("S7 RESULTS");
        Console.WriteLine(new string('=', 72));
        Console.WriteLine($"  baseline foreground : {before}");
        for (var index = 0; index < samples.Count; index++)
        {
            Console.WriteLine($"  after toast {index + 1}       : {samples[index]}"
                              + (samples[index] == before ? "  (unchanged)" : "  <-- CHANGED"));
        }

        Console.WriteLine();
        Console.WriteLine($"  Q0 actually on screen : {Verdict(rendered)}{Source(rendered)}");
        Console.WriteLine($"  Q1 focus never moved  : {Verdict(kept && !stolen)}   (measured)");
        Console.WriteLine($"  Q2 inside working area: {Verdict(placement)}{Source(placement)}");
        Console.WriteLine($"  Q3 two of them stack  : {Verdict(stacked)}{Source(stacked)}");
        Console.WriteLine($"  Q4 above other windows: {Verdict(above)}{Source(above)}");
        Console.WriteLine();

        if (stolen)
        {
            Console.WriteLine("  NOTE: a sample named this process — ShowActivated:false did not hold,");
            Console.WriteLine("        which disqualifies the option rather than merely marking it down.");
        }

        // Q0 and Q1 together are the verdict, and neither is sufficient alone: a window that never
        // rendered also never takes focus, so "focus did not move" only means something once the
        // shell agrees the window is on screen. Q2 to Q4 are appearance, which is work rather than
        // risk, so they are reported and do not decide.
        var passed = rendered != false && kept && !stolen;

        Console.WriteLine($"S7 VERDICT: {(passed
            ? "PASS - an application-drawn notification is shown without disturbing the foreground application"
            : "FAIL - see the rows above")}");

        return passed ? 0 : 1;
    }

    /// <summary>
    /// The foreground application, as a stable string so that two samples can simply be compared.
    /// </summary>
    private static string Foreground()
    {
        try
        {
            return IsMacOS ? MacForeground() : WindowsForeground();
        }
        catch (Exception exception)
        {
            return $"<unreadable: {exception.GetType().Name}>";
        }
    }

    private static string WindowsForeground()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return "<none>";
        }

        NativeMethods.GetWindowThreadProcessId(window, out var pid);

        try
        {
            return $"{Process.GetProcessById((int)pid).ProcessName} (pid {pid})";
        }
        catch (ArgumentException)
        {
            return $"<exited> (pid {pid})";
        }
    }

    private static string MacForeground()
    {
        var info = new ProcessStartInfo("osascript") {RedirectStandardOutput = true, CreateNoWindow = true};
        info.ArgumentList.Add("-e");
        info.ArgumentList.Add("tell application \"System Events\" to get name of first application process whose frontmost is true");

        using var process = Process.Start(info)!;
        var name = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        return string.IsNullOrEmpty(name) ? "<none>" : name;
    }

    private static bool IsThisProcess(string sample)
    {
        return sample.Contains($"pid {Environment.ProcessId}", StringComparison.Ordinal)
               || sample.StartsWith(Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);
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

    private static string Verdict(bool? value)
    {
        return value switch {true => "PASS", false => "FAIL", _ => "unanswered"};
    }

    /// <summary>Marks a row as measured rather than eyeballed, so the two are never confused later.</summary>
    private static string Source(bool? value)
    {
        return value is null ? string.Empty : OperatingSystem.IsWindows() ? "   (measured)" : "   (observed)";
    }

    /// <summary>
    /// Folds a per-toast measurement into one answer, or <c>null</c> where nothing was measured —
    /// which is every platform but Windows, and Windows with no probes taken.
    /// </summary>
    private static bool? Measured(List<Probe> probes, Func<Probe, bool?> select)
    {
        if (probes.Count == 0)
        {
            return null;
        }

        var values = probes.Select(select).ToList();

        return values.Any(value => value is null) ? null : values.All(value => value == true);
    }

    /// <summary>
    /// What the shell says about one toast: whether it is on screen, where, and whether it is the
    /// window a click at its own centre would land on — which is z-order measured rather than judged.
    /// </summary>
    private sealed record Probe(IntPtr Handle, bool? Visible, PixelRect Rect, bool? OnTopAtItsCentre)
    {
        private const uint GaRoot = 2;

        public static Probe Of(Toast toast)
        {
            var handle = toast.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

            if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero)
            {
                return new Probe(handle, null, default, null);
            }

            var visible = NativeMethods.IsWindowVisible(handle);

            if (!NativeMethods.GetWindowRect(handle, out var rect))
            {
                return new Probe(handle, visible, default, null);
            }

            var bounds = new PixelRect(
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top);

            // WindowFromPoint returns the deepest child, so the result is walked back up to its root
            // before being compared — otherwise the toast's own client area reads as "some other
            // window" and Q4 fails for no reason.
            var centre = new Point {X = bounds.X + bounds.Width / 2, Y = bounds.Y + bounds.Height / 2};
            var hit = NativeMethods.WindowFromPoint(centre);
            var root = hit == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hit, GaRoot);

            return new Probe(handle, visible, bounds, root == handle);
        }

        public override string ToString()
        {
            return $"hwnd 0x{Handle:X}  visible={Visible?.ToString() ?? "?"}  "
                   + $"rect={Rect}  topmost-at-centre={OnTopAtItsCentre?.ToString() ?? "?"}";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;

        public int Y;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr window, uint flags);
    }
}

/// <summary>
/// One notification, drawn by this application. Borderless, never activated, always on top, and
/// absent from the taskbar and the alt-tab list.
/// </summary>
internal sealed class Toast : Window
{
    private const double ToastWidth = 360;

    private const double ToastHeight = 96;

    private const double Gap = 8;

    private const double EdgeMargin = 16;

    public Toast(string title, string body)
    {
        // ShowActivated is the whole question: Win32 turns it into SW_SHOWNOACTIVATE and macOS into
        // a bare orderFront:, which by Apple's definition changes neither the key nor the main
        // window. Topmost is separate — it decides z-order, not focus.
        ShowActivated = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        Width = ToastWidth;
        Height = ToastHeight;
        Title = title;

        // Explicit brushes rather than a theme: no FluentTheme is loaded here, so nothing but this
        // file decides what appears.
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e));

        Content = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3c, 0x3c, 0x3c)),
            Padding = new Thickness(16, 14),
            Child = new StackPanel
            {
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 14,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White,
                    },
                    new TextBlock
                    {
                        Text = body,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xbe, 0xbe, 0xbe)),
                    },
                },
            },
        };
    }

    /// <summary>
    /// Places this toast in the notification corner, <paramref name="slot"/> places along the stack.
    /// </summary>
    /// <remarks>
    /// The corner is not the same on both platforms and this deliberately does not pretend it is:
    /// Windows notifications rise from the bottom right above the taskbar, macOS ones descend from
    /// the top right below the menu bar. <see cref="Screen.WorkingArea"/> is what keeps both clear
    /// of the taskbar, the Dock and the menu bar, and <see cref="Screen.Scaling"/> is needed because
    /// <see cref="Window.Position"/> is in physical pixels while Width and Height are not.
    /// </remarks>
    public void PlaceInCorner(int slot)
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();

        if (screen is null)
        {
            return;
        }

        var scale = screen.Scaling;
        var width = (int)(ToastWidth * scale);
        var height = (int)(ToastHeight * scale);
        var margin = (int)(EdgeMargin * scale);
        var step = height + (int)(Gap * scale);
        var area = screen.WorkingArea;

        var x = area.X + area.Width - width - margin;
        var y = OperatingSystem.IsMacOS()
            ? area.Y + margin + slot * step
            : area.Y + area.Height - height - margin - slot * step;

        Position = new PixelPoint(x, y);
    }

    /// <summary>Prints the geometry the placement was derived from, and returns the working area it used.</summary>
    public PixelRect ReportGeometry()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();

        Console.WriteLine($"screens           : {Screens.ScreenCount}");
        Console.WriteLine($"primary bounds    : {screen?.Bounds}");
        Console.WriteLine($"primary working   : {screen?.WorkingArea}   <-- clear of taskbar / Dock");
        Console.WriteLine($"primary scaling   : {screen?.Scaling}");
        Console.WriteLine($"toast position    : {Position}  ({ToastWidth} x {ToastHeight} logical)");
        Console.WriteLine();

        return screen?.WorkingArea ?? default;
    }
}
