namespace Pisum.Whisper.Core.Tests.Logging;

using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// A sink the tests put behind the application's own asynchronous wrapper, so what is exercised is
/// the wiring rather than a reconstruction of it. <see cref="Delay"/> makes it slow on demand, which
/// is how the buffer is driven into overflow.
/// </summary>
internal sealed class RecordingSink : ILogEventSink
{
    private readonly List<LogEvent> _events = [];

    public TimeSpan Delay { get; set; }

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_events)
            {
                return [.. _events.Select(logEvent => logEvent.RenderMessage())];
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_events)
            {
                return _events.Count;
            }
        }
    }

    /// <summary>
    /// The events themselves, for assertions that have to reach past the rendered message into the
    /// attached properties.
    /// </summary>
    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public void Emit(LogEvent logEvent)
    {
        if (Delay > TimeSpan.Zero)
        {
            Thread.Sleep(Delay);
        }

        lock (_events)
        {
            _events.Add(logEvent);
        }
    }

    /// <summary>
    /// Waits for the background worker to deliver, so assertions run against a drained queue rather
    /// than against a race.
    /// </summary>
    public bool WaitUntil(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    public bool WaitForMessageContaining(string fragment)
    {
        return WaitUntil(() => Messages.Any(message => message.Contains(fragment, StringComparison.Ordinal)));
    }
}
