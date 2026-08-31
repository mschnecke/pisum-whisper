namespace Pisum.Whisper.Core.Logging;

using Serilog.Sinks.Async;

/// <summary>
/// Counts what the asynchronous sink threw away. The sink drops rather than blocks under sustained
/// overload — log backpressure must never become audio backpressure — which makes the loss silent,
/// and a diagnostics subsystem does not get to lose events invisibly.
/// </summary>
public sealed class DroppedLogEventMonitor : IAsyncLogEventSinkMonitor
{
    private volatile IAsyncLogEventSinkInspector? _inspector;

    /// <summary>How many events the buffer has discarded, or zero while no asynchronous sink is attached.</summary>
    public long DroppedMessagesCount => _inspector?.DroppedMessagesCount ?? 0;

    public void StartMonitoring(IAsyncLogEventSinkInspector inspector)
    {
        _inspector = inspector;
    }

    /// <summary>
    /// Deliberately empty. The inspector is kept after the sink lets it go, because the count is read
    /// at shutdown and forgetting the inspector, there would report zero for every run.
    /// </summary>
    public void StopMonitoring(IAsyncLogEventSinkInspector inspector)
    {
    }
}
