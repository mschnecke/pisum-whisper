namespace Pisum.Whisper.Core.Hotkeys;

using SharpHook.Data;

/// <summary>What the hook handler must do about one key event.</summary>
/// <param name="Edge">The edge to report, or <see langword="null"/> if this event reports nothing.</param>
/// <param name="Suppress">Whether the event must be withheld from the focused application.</param>
public readonly record struct MatchResult(HotkeyEdge? Edge, bool Suppress)
{
    /// <summary>The event is none of our business: report nothing, withhold nothing.</summary>
    public static readonly MatchResult Ignore = new(null, false);
}

/// <summary>
/// The binding state machine. It is deliberately separate from the hook: it is pure state and
/// decisions, so every rule below is unit-tested without a keyboard, a hook or a platform.
/// </summary>
/// <remarks>
/// <para>
/// The hook handler runs on libuiohook's own callback thread, which both platforms police — Windows
/// removes a low-level hook that exceeds <c>LowLevelHooksTimeout</c>, macOS disables a tap that
/// stops responding — so everything here is a handful of comparisons under a lock that is never
/// contended in practice. Key events arrive milliseconds apart and a rebind is a user action; the
/// uncontended lock costs tens of nanoseconds against a budget of a second or more.
/// </para>
/// <para>
/// Suppression is tracked by key rather than by a flag. The main key's press and its release must be
/// withheld together — an application that sees a key go up that never went down is left in a worse
/// state than one that saw both — and the two are separated by the whole dictation, during which the
/// binding may have been disengaged by a modifier release or replaced by a rebind.
/// </para>
/// </remarks>
public sealed class HotkeyMatcher
{
    private readonly Lock _gate = new();

    private HotkeyChord _chord;

    private bool _engaged;

    private KeyCode? _suppressedKey;

    public HotkeyMatcher(HotkeyChord chord)
    {
        _chord = chord;
    }

    /// <summary>The binding currently being matched.</summary>
    public HotkeyChord Chord
    {
        get
        {
            lock (_gate)
            {
                return _chord;
            }
        }
    }

    /// <summary>Whether the binding is currently held.</summary>
    public bool IsEngaged
    {
        get
        {
            lock (_gate)
            {
                return _engaged;
            }
        }
    }

    /// <summary>Decides what a key press means.</summary>
    public MatchResult OnKeyPressed(KeyCode keyCode, EventMask mask, bool isSimulated)
    {
        // Our own paste keystroke must not be observed, and neither must anything else the
        // application injects.
        if (isSimulated)
        {
            return MatchResult.Ignore;
        }

        lock (_gate)
        {
            // Auto-repeat. Both platforms raise repeated presses while a key is held, and every one
            // of them has to keep being withheld, but only the first is an edge.
            if (_suppressedKey == keyCode)
            {
                return new MatchResult(null, true);
            }

            if (keyCode != _chord.Key || ModifierGroups.FromEventMask(mask) != _chord.Modifiers)
            {
                return MatchResult.Ignore;
            }

            _engaged = true;
            _suppressedKey = keyCode;
            return new MatchResult(HotkeyEdge.Pressed, true);
        }
    }

    /// <summary>Decides what a key release means.</summary>
    public MatchResult OnKeyReleased(KeyCode keyCode, EventMask mask, bool isSimulated)
    {
        if (isSimulated)
        {
            return MatchResult.Ignore;
        }

        lock (_gate)
        {
            var suppress = _suppressedKey == keyCode;
            if (suppress)
            {
                _suppressedKey = null;
            }

            if (!_engaged)
            {
                return new MatchResult(null, suppress);
            }

            // The chord breaks on the main key or on any modifier it requires, not on the main key
            // alone: letting go of Ctrl while still holding Space would otherwise keep the binding
            // engaged while a bare, unwithheld Space repeated into the focused application.
            var releasedGroup = ModifierGroups.FromKeyCode(keyCode);
            var breaksChord = keyCode == _chord.Key || (releasedGroup & _chord.Modifiers) != 0;

            if (!breaksChord)
            {
                return new MatchResult(null, suppress);
            }

            _engaged = false;

            // A modifier is never withheld: applications derive modifier state from the events they
            // receive, so swallowing a Ctrl release leaves them believing Ctrl is still down.
            return new MatchResult(HotkeyEdge.Released, suppress);
        }
    }

    /// <summary>
    /// Adopts a new binding, reporting whether the previous one was engaged and therefore needs a
    /// release. The key withheld from the previous binding stays tracked, so its eventual release is
    /// still withheld and the focused application never sees an unpaired key up.
    /// </summary>
    public bool Rebind(HotkeyChord chord)
    {
        lock (_gate)
        {
            if (_chord == chord)
            {
                return false;
            }

            _chord = chord;

            if (!_engaged)
            {
                return false;
            }

            _engaged = false;
            return true;
        }
    }

    /// <summary>
    /// Ends any engagement, reporting whether one was in progress. Called when observation stops
    /// without the physical release being seen.
    /// </summary>
    public bool Disengage()
    {
        lock (_gate)
        {
            if (!_engaged)
            {
                return false;
            }

            _engaged = false;
            _suppressedKey = null;
            return true;
        }
    }
}
