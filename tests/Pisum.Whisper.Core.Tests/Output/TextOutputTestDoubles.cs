namespace Pisum.Whisper.Core.Tests.Output;

using Pisum.Whisper.Core.Output;

/// <summary>
/// An in-memory clipboard that records every write, so the tests can assert what was put on it and
/// in what order without a real one — which no CI agent reliably has, and which the sequence logic
/// does not need.
/// </summary>
public sealed class FakeClipboard : ISystemClipboard
{
    private readonly Lock _gate = new();

    private readonly List<string> _writes = [];

    private string? _text;

    private int _reads;

    /// <summary>Set to make <see cref="TryGetText"/> throw, which the sequence treats as best effort.</summary>
    public Exception? ReadFailure { get; set; }

    /// <summary>Set to make <see cref="SetText"/> throw, which is the only hard failure of a delivery.</summary>
    public Exception? WriteFailure { get; set; }

    public string? Text
    {
        get
        {
            lock (_gate)
            {
                return _text;
            }
        }

        set
        {
            lock (_gate)
            {
                _text = value;
            }
        }
    }

    public IReadOnlyList<string> Writes
    {
        get
        {
            lock (_gate)
            {
                return [.. _writes];
            }
        }
    }

    public int Reads
    {
        get
        {
            lock (_gate)
            {
                return _reads;
            }
        }
    }

    public string? TryGetText()
    {
        if (ReadFailure is { } failure)
        {
            throw failure;
        }

        lock (_gate)
        {
            _reads++;
            return _text;
        }
    }

    public void SetText(string text)
    {
        if (WriteFailure is { } failure)
        {
            throw failure;
        }

        lock (_gate)
        {
            _text = text;
            _writes.Add(text);
        }
    }
}

/// <summary>
/// Stands in for the platform's answer to "can synthetic input reach the focused application".
/// </summary>
public sealed class FakePasteProbe : IPasteProbe
{
    public bool Allow { get; set; } = true;

    public int Calls { get; private set; }

    public bool CanPaste()
    {
        Calls++;
        return Allow;
    }
}
