namespace Pisum.Whisper.Core.Tests.Hotkeys;

using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Settings;
using Shouldly;

/// <summary>
/// The conflict table is warn-only, so these tests are about what it reports, never about what it
/// prevents. The default binding reporting no conflict is the one that would be noticed in practice.
/// </summary>
[TestClass]
public sealed class ConflictDetectorTests
{
    private static HotkeyBinding Binding(string key, params string[] modifiers)
    {
        return new HotkeyBinding { Modifiers = [.. modifiers], Key = key };
    }

    [TestMethod]
    public void ExactSystemShortcut_IsReportedAsConflicting()
    {
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Tab", "Alt")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("F4", "Alt")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Space", "Ctrl")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Escape", "Ctrl", "Shift")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Delete", "Ctrl", "Alt")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("4", "Cmd", "Shift")).ShouldBeTrue();
    }

    [TestMethod]
    public void ModifierOrder_DoesNotMatter()
    {
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Escape", "Ctrl", "Shift")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Escape", "Shift", "Ctrl")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("3", "Shift", "Cmd")).ShouldBeTrue();
    }

    [TestMethod]
    public void ModifierSpelling_DoesNotMatter()
    {
        // Win+L and Cmd+L are the same entry once folded, which is what lets one table serve both
        // platforms.
        ConflictDetector.ConflictsWithSystemHotkey(Binding("L", "Win")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("L", "Cmd")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("L", "Meta")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("l", "super")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Q", "COMMAND")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Space", "Control")).ShouldBeTrue();
    }

    [TestMethod]
    public void KeyAliases_ConflictLikeTheirPrimaryName()
    {
        // Resolving the key through the vocabulary rather than comparing strings is what makes this
        // hold; the reference compares lowercased text and would miss it.
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Del", "Ctrl", "Alt")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Esc", "Ctrl", "Shift")).ShouldBeTrue();
    }

    [TestMethod]
    public void DefaultBinding_ReportsNoConflict()
    {
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Space", "Ctrl", "Shift")).ShouldBeFalse();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Space", "Cmd", "Shift")).ShouldBeFalse();

        // Whatever this platform's default is, it must not warn on first launch.
        ConflictDetector.ConflictsWithSystemHotkey(new HotkeyBinding()).ShouldBeFalse();
    }

    [TestMethod]
    public void UnrelatedBinding_ReportsNoConflict()
    {
        ConflictDetector.ConflictsWithSystemHotkey(Binding("F9")).ShouldBeFalse();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("J", "Ctrl", "Alt")).ShouldBeFalse();
    }

    [TestMethod]
    public void ExtraModifier_BreaksTheMatch()
    {
        // Ctrl+Shift+Space is not Ctrl+Space, which is why the comparison is equality rather than
        // containment.
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Space", "Ctrl", "Shift")).ShouldBeFalse();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Tab", "Alt", "Shift")).ShouldBeFalse();
    }

    [TestMethod]
    public void BlankModifierEntry_IsSkippedRatherThanRejected()
    {
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Tab", "Alt", "")).ShouldBeTrue();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Tab", "  ", "Alt")).ShouldBeTrue();
    }

    [TestMethod]
    public void UnresolvableBinding_ReportsNoConflict()
    {
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Tab", "Hyper")).ShouldBeFalse();
        ConflictDetector.ConflictsWithSystemHotkey(Binding("Nonsense", "Alt")).ShouldBeFalse();
    }
}
