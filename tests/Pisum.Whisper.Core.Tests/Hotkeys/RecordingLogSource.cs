namespace Pisum.Whisper.Core.Tests.Hotkeys;

using SharpHook.Data;
using SharpHook.Logging;

/// <summary>
/// A libuiohook log source the tests can raise messages through, standing in for the real one so
/// no native logging is registered by a unit test.
/// </summary>
public sealed class RecordingLogSource : ILogSource
{
    public event EventHandler<LogEventArgs>? MessageLogged;

    public bool IsDisposed { get; private set; }

    public void Raise(LogLevel level, string text)
    {
        MessageLogged?.Invoke(this, new LogEventArgs(new LogEntry(
            level,
            text,
            text,
            text,
            [],
            [],
            [])));
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
