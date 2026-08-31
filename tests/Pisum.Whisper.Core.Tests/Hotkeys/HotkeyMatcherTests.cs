namespace Pisum.Whisper.Core.Tests.Hotkeys;

using Pisum.Whisper.Core.Hotkeys;
using SharpHook.Data;
using Shouldly;

/// <summary>
/// The binding rules, exercised without a keyboard or a hook. The default binding is used
/// throughout: Ctrl+Shift+Space.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class HotkeyMatcherTests
{
    private const EventMask CtrlShift = EventMask.LeftCtrl | EventMask.LeftShift;

    private static HotkeyMatcher Matcher(HotkeyModifiers modifiers = HotkeyModifiers.Ctrl | HotkeyModifiers.Shift,
                                         KeyCode key = KeyCode.VcSpace)
    {
        return new HotkeyMatcher(new HotkeyChord(modifiers, key));
    }

    private static MatchResult Press(HotkeyMatcher matcher, KeyCode key, EventMask mask = CtrlShift)
    {
        return matcher.OnKeyPressed(key, mask, false);
    }

    private static MatchResult Release(HotkeyMatcher matcher, KeyCode key, EventMask mask = CtrlShift)
    {
        return matcher.OnKeyReleased(key, mask, false);
    }

    // ---- Task 2.3: the match predicate is exact equality, not containment ----

    [Fact]
    public void ConfiguredCombination_Matches()
    {
        var matcher = Matcher();

        Press(matcher, KeyCode.VcSpace).Edge.ShouldBe(HotkeyEdge.Pressed);
        matcher.IsEngaged.ShouldBeTrue();
    }

    [Fact]
    public void AdditionalModifier_DoesNotMatch()
    {
        var matcher = Matcher();

        Press(matcher, KeyCode.VcSpace, CtrlShift | EventMask.LeftAlt).ShouldBe(MatchResult.Ignore);
        matcher.IsEngaged.ShouldBeFalse();
    }

    [Fact]
    public void MissingModifier_DoesNotMatch()
    {
        var matcher = Matcher();

        Press(matcher, KeyCode.VcSpace, EventMask.LeftCtrl).ShouldBe(MatchResult.Ignore);
        Press(matcher, KeyCode.VcSpace, EventMask.None).ShouldBe(MatchResult.Ignore);
        matcher.IsEngaged.ShouldBeFalse();
    }

    [Fact]
    public void ModifierlessBinding_DoesNotMatchWhileAModifierIsHeld()
    {
        var matcher = Matcher(HotkeyModifiers.None, KeyCode.VcF9);

        Press(matcher, KeyCode.VcF9, EventMask.LeftCtrl).ShouldBe(MatchResult.Ignore);
        Press(matcher, KeyCode.VcF9, EventMask.None).Edge.ShouldBe(HotkeyEdge.Pressed);
    }

    [Fact]
    public void RightHandModifiers_Match()
    {
        var matcher = Matcher();

        Press(matcher, KeyCode.VcSpace, EventMask.RightCtrl | EventMask.RightShift)
            .Edge.ShouldBe(HotkeyEdge.Pressed);
    }

    [Fact]
    public void LockKeysAndMouseButtons_DoNotBreakTheMatch()
    {
        var matcher = Matcher();
        var mask = CtrlShift | EventMask.CapsLock | EventMask.NumLock | EventMask.Button1;

        Press(matcher, KeyCode.VcSpace, mask).Edge.ShouldBe(HotkeyEdge.Pressed);
    }

    // ---- Task 2.4: engage, coalesce auto-repeat, disengage when the chord breaks ----

    [Fact]
    public void OnePressAndRelease_ReportsOneEdgeEach()
    {
        var matcher = Matcher();

        Press(matcher, KeyCode.VcSpace).Edge.ShouldBe(HotkeyEdge.Pressed);
        Release(matcher, KeyCode.VcSpace).Edge.ShouldBe(HotkeyEdge.Released);
        matcher.IsEngaged.ShouldBeFalse();
    }

    [Fact]
    public void AutoRepeat_ReportsExactlyOnePress()
    {
        var matcher = Matcher();
        var edges = new List<HotkeyEdge>();

        for (var repeat = 0; repeat < 20; repeat++)
        {
            var result = Press(matcher, KeyCode.VcSpace);
            if (result.Edge is { } edge)
            {
                edges.Add(edge);
            }

            result.Suppress.ShouldBeTrue("every repeat must keep being withheld");
        }

        edges.ShouldBe([HotkeyEdge.Pressed]);

        Release(matcher, KeyCode.VcSpace).Edge.ShouldBe(HotkeyEdge.Released);
    }

    [Fact]
    public void ModifierReleasedFirst_EndsTheHold()
    {
        var matcher = Matcher();
        Press(matcher, KeyCode.VcSpace);

        Release(matcher, KeyCode.VcLeftShift, EventMask.LeftCtrl).Edge.ShouldBe(HotkeyEdge.Released);
        matcher.IsEngaged.ShouldBeFalse();
    }

    [Fact]
    public void MainKeyReleasedFirst_EndsTheHold()
    {
        var matcher = Matcher();
        Press(matcher, KeyCode.VcSpace);

        Release(matcher, KeyCode.VcSpace).Edge.ShouldBe(HotkeyEdge.Released);
        matcher.IsEngaged.ShouldBeFalse();
    }

    [Fact]
    public void RemainingKeysReleasedAfterwards_ReportNothingFurther()
    {
        var matcher = Matcher();
        Press(matcher, KeyCode.VcSpace);
        Release(matcher, KeyCode.VcLeftShift, EventMask.LeftCtrl).Edge.ShouldBe(HotkeyEdge.Released);

        Release(matcher, KeyCode.VcSpace, EventMask.LeftCtrl).Edge.ShouldBeNull();
        Release(matcher, KeyCode.VcLeftControl, EventMask.None).Edge.ShouldBeNull();
    }

    [Fact]
    public void UnrelatedModifierRelease_DoesNotEndTheHold()
    {
        var matcher = Matcher();
        Press(matcher, KeyCode.VcSpace, CtrlShift | EventMask.LeftAlt | EventMask.CapsLock);
        matcher.IsEngaged.ShouldBeFalse("Alt is not part of the binding, so it should not have matched");

        matcher = Matcher();
        Press(matcher, KeyCode.VcSpace);

        // Alt is not required by the binding, so letting go of it changes nothing.
        Release(matcher, KeyCode.VcLeftAlt).Edge.ShouldBeNull();
        matcher.IsEngaged.ShouldBeTrue();
    }

    [Fact]
    public void UnrelatedKey_ReportsNothing()
    {
        var matcher = Matcher();

        Press(matcher, KeyCode.VcA).ShouldBe(MatchResult.Ignore);
        Release(matcher, KeyCode.VcA).ShouldBe(MatchResult.Ignore);

        Press(matcher, KeyCode.VcSpace);
        Press(matcher, KeyCode.VcA).ShouldBe(MatchResult.Ignore);
        matcher.IsEngaged.ShouldBeTrue("typing while holding the binding must not end the hold");
    }

    [Fact]
    public void ReleaseWithoutPress_ReportsNothing()
    {
        var matcher = Matcher();

        Release(matcher, KeyCode.VcSpace).ShouldBe(MatchResult.Ignore);
        Release(matcher, KeyCode.VcLeftShift).ShouldBe(MatchResult.Ignore);
    }

    // ---- Task 2.5: the main key is withheld, modifiers never are ----

    [Fact]
    public void MatchedMainKey_IsWithheldOnBothEdges()
    {
        var matcher = Matcher();

        Press(matcher, KeyCode.VcSpace).Suppress.ShouldBeTrue();
        Release(matcher, KeyCode.VcSpace).Suppress.ShouldBeTrue();
    }

    [Fact]
    public void ModifierKeys_AreNeverWithheld()
    {
        var matcher = Matcher();

        Press(matcher, KeyCode.VcLeftControl, EventMask.LeftCtrl).Suppress.ShouldBeFalse();
        Press(matcher, KeyCode.VcLeftShift).Suppress.ShouldBeFalse();

        Press(matcher, KeyCode.VcSpace);
        Release(matcher, KeyCode.VcLeftShift, EventMask.LeftCtrl).Suppress.ShouldBeFalse();
        Release(matcher, KeyCode.VcLeftControl, EventMask.None).Suppress.ShouldBeFalse();
    }

    [Fact]
    public void UnmatchedMainKey_IsNotWithheld()
    {
        var matcher = Matcher();

        // Space pressed on its own belongs to whatever has focus.
        Press(matcher, KeyCode.VcSpace, EventMask.None).Suppress.ShouldBeFalse();
        Release(matcher, KeyCode.VcSpace, EventMask.None).Suppress.ShouldBeFalse();
    }

    [Fact]
    public void MainKeyRelease_IsWithheldEvenAfterAModifierEndedTheHold()
    {
        var matcher = Matcher();
        Press(matcher, KeyCode.VcSpace).Suppress.ShouldBeTrue();
        Release(matcher, KeyCode.VcLeftShift, EventMask.LeftCtrl);

        // The press was swallowed, so the release must be too. An application that sees a key go up
        // it never saw go down is left worse off than one that saw neither.
        Release(matcher, KeyCode.VcSpace, EventMask.LeftCtrl).Suppress.ShouldBeTrue();
    }

    // ---- Task 2.6: the application does not observe its own synthetic input ----

    [Fact]
    public void SimulatedPress_ReportsNothingAndIsNotWithheld()
    {
        var matcher = Matcher();

        matcher.OnKeyPressed(KeyCode.VcSpace, CtrlShift, true).ShouldBe(MatchResult.Ignore);
        matcher.IsEngaged.ShouldBeFalse();
    }

    [Fact]
    public void SimulatedRelease_DoesNotEndARealHold()
    {
        var matcher = Matcher();
        Press(matcher, KeyCode.VcSpace);

        matcher.OnKeyReleased(KeyCode.VcSpace, CtrlShift, true).ShouldBe(MatchResult.Ignore);
        matcher.IsEngaged.ShouldBeTrue();
    }

    // ---- Rebind and teardown, consumed by tasks 3.4 and 3.6 ----

    [Fact]
    public void Rebind_WhileEngaged_ReportsThatAReleaseIsOwed()
    {
        var matcher = Matcher();
        Press(matcher, KeyCode.VcSpace);

        matcher.Rebind(new HotkeyChord(HotkeyModifiers.Alt, KeyCode.VcF9)).ShouldBeTrue();
        matcher.IsEngaged.ShouldBeFalse();

        // The old main key is still physically down and its press was swallowed, so its release is
        // still withheld even though the binding it belonged to is gone.
        Release(matcher, KeyCode.VcSpace).Suppress.ShouldBeTrue();
    }

    [Fact]
    public void Rebind_WhileIdle_OwesNothing()
    {
        var matcher = Matcher();

        matcher.Rebind(new HotkeyChord(HotkeyModifiers.Alt, KeyCode.VcF9)).ShouldBeFalse();
        matcher.Chord.Key.ShouldBe(KeyCode.VcF9);
    }

    [Fact]
    public void Rebind_ToTheSameChord_IsANoOp()
    {
        var matcher = Matcher();
        Press(matcher, KeyCode.VcSpace);

        matcher.Rebind(new HotkeyChord(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, KeyCode.VcSpace))
            .ShouldBeFalse();
        matcher.IsEngaged.ShouldBeTrue("an unchanged binding must not interrupt a hold in progress");
    }

    [Fact]
    public void Disengage_ReportsWhetherAReleaseIsOwed()
    {
        var matcher = Matcher();

        matcher.Disengage().ShouldBeFalse();

        Press(matcher, KeyCode.VcSpace);
        matcher.Disengage().ShouldBeTrue();
        matcher.Disengage().ShouldBeFalse();
    }
}
