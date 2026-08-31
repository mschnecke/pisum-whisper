namespace Pisum.Whisper.Core.Tests.Hotkeys;

using SharpHook.Data;
using SharpHook.Providers;

/// <summary>
/// A hook provider that blocks forever instead of starting, reproducing what change 1's macOS spike
/// observed on an Apple M4 with no Accessibility grant: libuiohook does not fail and does not prompt,
/// it simply never returns, at zero CPU, with the event tap never installed.
/// </summary>
public sealed class BlockingHookProvider : IGlobalHookProvider
{
    private readonly ManualResetEventSlim _released = new();

    public bool KeyTypedEnabled { get; set; }

    public void SetDispatchProc(DispatchProc? dispatchProc, nint userData)
    {
    }

    public UioHookResult Run()
    {
        return Block();
    }

    public UioHookResult RunKeyboard()
    {
        return Block();
    }

    public UioHookResult RunMouse()
    {
        return Block();
    }

    public UioHookResult Stop()
    {
        _released.Set();
        return UioHookResult.Success;
    }

    private UioHookResult Block()
    {
        _released.Wait();
        return UioHookResult.Success;
    }
}
