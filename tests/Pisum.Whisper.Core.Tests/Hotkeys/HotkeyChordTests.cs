namespace Pisum.Whisper.Core.Tests.Hotkeys;

using Pisum.Whisper.Core.Hotkeys;
using Pisum.Whisper.Core.Settings;
using SharpHook.Data;
using Shouldly;

[UnitTest]
public sealed class HotkeyChordTests
{
    private static HotkeyBinding Binding(string key, params string[] modifiers)
    {
        return new HotkeyBinding {Modifiers = [.. modifiers], Key = key};
    }

    [Fact]
    public void DefaultBinding_CompilesToItsGroupsAndKey()
    {
        HotkeyChord.TryCompile(Binding("Space", "Ctrl", "Shift"), out var chord, out _).ShouldBeTrue();

        chord.Modifiers.ShouldBe(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
        chord.Key.ShouldBe(KeyCode.VcSpace);
    }

    [Fact]
    public void ModifierSpellings_CompileToTheSameChord()
    {
        HotkeyChord.TryCompile(Binding("Space", "Cmd", "Shift"), out var mac, out _).ShouldBeTrue();
        HotkeyChord.TryCompile(Binding("Space", "Win", "Shift"), out var windows, out _).ShouldBeTrue();

        mac.ShouldBe(windows);
    }

    [Fact]
    public void BindingWithNoModifiers_CompilesToNone()
    {
        HotkeyChord.TryCompile(Binding("F9"), out var chord, out _).ShouldBeTrue();

        chord.Modifiers.ShouldBe(HotkeyModifiers.None);
        chord.Key.ShouldBe(KeyCode.VcF9);
    }

    [Fact]
    public void BlankModifierEntry_IsSkipped()
    {
        HotkeyChord.TryCompile(Binding("Space", "Ctrl", "", "  "), out var chord, out _).ShouldBeTrue();

        chord.Modifiers.ShouldBe(HotkeyModifiers.Ctrl);
    }

    [Fact]
    public void UnknownKey_FailsWithoutThrowing_AndNamesTheToken()
    {
        HotkeyChord.TryCompile(Binding("Nonsense", "Ctrl"), out _, out var token).ShouldBeFalse();
        token.ShouldBe("Nonsense");
    }

    [Fact]
    public void UnknownModifier_FailsWithoutThrowing_AndNamesTheToken()
    {
        HotkeyChord.TryCompile(Binding("Space", "Ctrl", "Hyper"), out _, out var token).ShouldBeFalse();
        token.ShouldBe("Hyper");
    }

    [Fact]
    public void Default_MatchesThisPlatformsSettingsDefault()
    {
        HotkeyChord.TryCompile(new HotkeyBinding(), out var fromSettings, out _).ShouldBeTrue();

        HotkeyChord.Default.ShouldBe(fromSettings);
        HotkeyChord.Default.Key.ShouldBe(KeyCode.VcSpace);
        HotkeyChord.Default.Modifiers.ShouldBe(
            OperatingSystem.IsMacOS()
                ? HotkeyModifiers.Meta | HotkeyModifiers.Shift
                : HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
    }

    [Fact]
    public void ToString_RendersTheBindingForLogging()
    {
        HotkeyChord.TryCompile(Binding("Space", "Shift", "Ctrl"), out var chord, out _).ShouldBeTrue();

        // Order is the chord's, not the settings file's, so the same binding always logs the same.
        chord.ToString().ShouldBe("Ctrl+Shift+Space");
    }

    [Fact]
    public void Chords_CompareByValue()
    {
        // The service swaps chords with a volatile write and compares to decide whether anything
        // changed, so value equality is load-bearing rather than incidental.
        var first = new HotkeyChord(HotkeyModifiers.Ctrl, KeyCode.VcSpace);
        var second = new HotkeyChord(HotkeyModifiers.Ctrl, KeyCode.VcSpace);

        first.ShouldBe(second);
        first.ShouldNotBe(new HotkeyChord(HotkeyModifiers.Alt, KeyCode.VcSpace));
    }
}
