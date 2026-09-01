namespace Pisum.Whisper.Platform.Tests.Autostart;

using System.Runtime.Versioning;
using Microsoft.Win32;
using Pisum.Whisper.Core.Autostart;
using Pisum.Whisper.Platform.Autostart;
using Shouldly;

/// <summary>
/// Task 4.2 — the Windows login registration, round-tripped against a real registry key.
/// </summary>
/// <remarks>
/// <para>
/// The key is a private one under <c>HKCU\Software\Pisum.Whisper.Tests</c>, deleted in
/// <see cref="Dispose"/>, which is the whole reason the subkey path is injected: without it the only
/// honest test writes to the user's own <c>Run</c> key.
/// </para>
/// <para>
/// <c>Integration</c>, by the rule in <c>CLAUDE.md</c> — this writes to the real registry, which is
/// neither a temp file nor a container but is plainly not <c>Unit</c>.
/// </para>
/// <para>
/// The platform attribute is what keeps <c>CA1416</c> quiet without a suppression at every call:
/// the runtime gate is <see cref="WindowsOnly"/> on each test and the guard in <see cref="Dispose"/>,
/// which the analyser cannot see.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class WindowsAutostartTests : IDisposable
{
    private const string TestRoot = @"Software\Pisum.Whisper.Tests";

    private readonly string _subKey = $@"{TestRoot}\{Guid.NewGuid():n}";

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Registry.CurrentUser.DeleteSubKeyTree(_subKey, throwOnMissingSubKey: false);
    }

    [Fact(
        Skip = "The Windows registry is not reachable on this operating system",
        SkipUnless = nameof(WindowsOnly.Enabled),
        SkipType = typeof(WindowsOnly))]
    public void EnablingRegistersTheRunningExecutable()
    {
        var autostart = Create();

        autostart.IsEnabled().ShouldBeFalse();

        autostart.Enable();

        autostart.IsEnabled().ShouldBeTrue();
        Value().ShouldBe($"\"{Environment.ProcessPath}\"");
    }

    /// <summary>
    /// The user gets one entry rather than two — the reason <c>SetValue</c> replaces rather than the
    /// registration being read first.
    /// </summary>
    [Fact(
        Skip = "The Windows registry is not reachable on this operating system",
        SkipUnless = nameof(WindowsOnly.Enabled),
        SkipType = typeof(WindowsOnly))]
    public void EnablingTwiceLeavesOneValue()
    {
        var autostart = Create();

        autostart.Enable();
        autostart.Enable();

        ValueCount().ShouldBe(1);
        autostart.IsEnabled().ShouldBeTrue();
    }

    [Fact(
        Skip = "The Windows registry is not reachable on this operating system",
        SkipUnless = nameof(WindowsOnly.Enabled),
        SkipType = typeof(WindowsOnly))]
    public void DisablingRemovesTheRegistration()
    {
        var autostart = Create();

        autostart.Enable();
        autostart.Disable();

        autostart.IsEnabled().ShouldBeFalse();
        ValueCount().ShouldBe(0);
    }

    /// <summary>Reconciling calls this when the setting is already off and nothing is registered.</summary>
    [Fact(
        Skip = "The Windows registry is not reachable on this operating system",
        SkipUnless = nameof(WindowsOnly.Enabled),
        SkipType = typeof(WindowsOnly))]
    public void DisablingWhenNotRegisteredDoesNothing()
    {
        var autostart = Create();

        Should.NotThrow(autostart.Disable);

        autostart.IsEnabled().ShouldBeFalse();
    }

    /// <summary>Nothing is created merely by asking, so a read never leaves a key behind.</summary>
    [Fact(
        Skip = "The Windows registry is not reachable on this operating system",
        SkipUnless = nameof(WindowsOnly.Enabled),
        SkipType = typeof(WindowsOnly))]
    public void ReadingAnAbsentKeyIsFalseRatherThanAFailure()
    {
        Create().IsEnabled().ShouldBeFalse();

        KeyExists().ShouldBeFalse();
    }

    private IAutostartService Create()
    {
        return new WindowsAutostart(_subKey);
    }

    private string? Value()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey);

        return key?.GetValue("Pisum Whisper") as string;
    }

    private int ValueCount()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey);

        return key?.GetValueNames().Length ?? 0;
    }

    private bool KeyExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey);

        return key is not null;
    }
}
