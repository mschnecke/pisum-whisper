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

        Registry.CurrentUser.DeleteSubKeyTree(_subKey, false);
    }

    [Fact(
        Skip = "The Windows registry is not reachable on this operating system",
        SkipUnless = nameof(WindowsOnly.Enabled),
        SkipType = typeof(WindowsOnly))]
    public void EnablingRegistersTheRunningExecutable()
    {
        var autostart = Create();

        autostart.Read().ShouldBe(AutostartRegistration.Absent);

        autostart.Enable();

        autostart.Read().ShouldBe(AutostartRegistration.Current);
        Value().ShouldBe($"\"{Environment.ProcessPath}\"");
    }

    /// <summary>
    /// A registration naming something else — the install that replaced the build a developer
    /// was running — is <c>Stale</c>, not <c>Absent</c> and emphatically not <c>Current</c>. Reading it
    /// as merely "registered" is what let the machine go on launching the old path at every login
    /// while the setting truthfully reported that start-at-login was on.
    /// </summary>
    [Fact(
        Skip = "The Windows registry is not reachable on this operating system",
        SkipUnless = nameof(WindowsOnly.Enabled),
        SkipType = typeof(WindowsOnly))]
    public void ARegistrationNamingAnotherExecutableIsStaleAndIsRepointedByEnabling()
    {
        var autostart = Create();

        Write("\"C:\\Program Files\\Somewhere Else\\Pisum.Whisper.App.exe\"");

        autostart.Read().ShouldBe(AutostartRegistration.Stale);

        autostart.Enable();

        autostart.Read().ShouldBe(AutostartRegistration.Current);
        Value().ShouldBe($"\"{Environment.ProcessPath}\"");
        ValueCount().ShouldBe(1);
    }

    /// <summary>
    /// An unquoted path is stale even when it names this very executable. The quoting is not
    /// cosmetic — without it a path under <i>Program Files</i> is read as a command and its first
    /// space as an argument separator — so a registration written that way is one that will not
    /// launch, and rewriting it is the point.
    /// </summary>
    [Fact(
        Skip = "The Windows registry is not reachable on this operating system",
        SkipUnless = nameof(WindowsOnly.Enabled),
        SkipType = typeof(WindowsOnly))]
    public void AnUnquotedRegistrationOfThisExecutableIsStale()
    {
        var autostart = Create();

        Write(Environment.ProcessPath!);

        autostart.Read().ShouldBe(AutostartRegistration.Stale);
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
        autostart.Read().ShouldBe(AutostartRegistration.Current);
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

        autostart.Read().ShouldBe(AutostartRegistration.Absent);
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

        autostart.Read().ShouldBe(AutostartRegistration.Absent);
    }

    /// <summary>Nothing is created merely by asking, so a read never leaves a key behind.</summary>
    [Fact(
        Skip = "The Windows registry is not reachable on this operating system",
        SkipUnless = nameof(WindowsOnly.Enabled),
        SkipType = typeof(WindowsOnly))]
    public void ReadingAnAbsentKeyIsAbsentRatherThanAFailure()
    {
        Create().Read().ShouldBe(AutostartRegistration.Absent);

        KeyExists().ShouldBeFalse();
    }

    private IAutostartService Create()
    {
        return new WindowsAutostart(_subKey);
    }

    /// <summary>Puts a registration there that this class did not write, which is the whole arrangement.</summary>
    private void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_subKey);

        key.SetValue("Pisum Whisper", command);
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
