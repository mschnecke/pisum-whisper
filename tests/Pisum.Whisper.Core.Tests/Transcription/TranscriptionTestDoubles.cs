namespace Pisum.Whisper.Core.Tests.Transcription;

using System.Net;
using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Transcription;

/// <summary>What the stub handler saw, snapshotted — the request message itself is disposed by the
/// caller before a test could read it.</summary>
internal sealed record RecordedRequest(Uri? RequestUri, string? ApiKey, string Body);

/// <summary>
/// A handler that answers from a scripted queue and records what it was asked. Every test in this
/// folder runs against it; nothing here touches the network.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public int SendCount => Requests.Count;

    /// <summary>Queues one response. The last queued response repeats once the queue runs dry.</summary>
    public StubHttpMessageHandler Respond(HttpStatusCode status, string body = "{}")
    {
        _responses.Enqueue(() => new HttpResponseMessage(status) {Content = new StringContent(body)});
        return this;
    }

    public StubHttpMessageHandler Throws(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                 CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var apiKey = request.Headers.TryGetValues(GeminiHttpClient.ApiKeyHeader, out var values)
            ? values.FirstOrDefault()
            : null;

        Requests.Add(new RecordedRequest(request.RequestUri, apiKey, body));

        cancellationToken.ThrowIfCancellationRequested();

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("The stub handler was called more times than it was scripted for.");
        }

        // The last scripted response stays in place, so "always fails" needs one entry rather than N.
        var next = _responses.Count == 1 ? _responses.Peek() : _responses.Dequeue();
        return next();
    }
}

/// <summary>Hands out clients over one handler, standing in for the container's factory.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient(handler, false) {BaseAddress = GeminiHttpClient.BaseAddress};
    }
}

/// <summary>Captures rendered log messages so the privacy assertions can read them.</summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel,
                            EventId eventId,
                            TState state,
                            Exception? exception,
                            Func<TState, Exception?, string> formatter)
    {
        lock (_messages)
        {
            _messages.Add(formatter(state, exception));
        }
    }
}
