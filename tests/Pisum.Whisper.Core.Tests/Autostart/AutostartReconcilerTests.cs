namespace Pisum.Whisper.Core.Tests.Autostart;

using Microsoft.Extensions.Logging;
using Pisum.Whisper.Core.Autostart;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Tests.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Extensions.Logging;
using Shouldly;

/// <summary>
/// Task 4.1 — the reconciler: it reads before it writes, writes only on a mismatch, and never stops
/// the application from starting.
/// </summary>
/// <remarks>
/// <c>Integration</c> rather than <c>Unit</c>, by the mechanical rule in <c>CLAUDE.md</c>: raising
/// <see cref="SettingsStore.Changed"/> means calling <see cref="SettingsStore.Save"/>, and that
/// writes a real file. The store carries no seam of its own — the reconciler's other dependency is
/// the one that is faked.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class AutostartReconcilerTests : IDisposable
{
    private readonly RecordingSink _sink = new();

    private readonly Logger _serilog;

    private readonly SerilogLoggerFactory _loggerFactory;

    private readonly string _home;

    private readonly SettingsStore _settings;

    private readonly FakeAutostartService _autostart = new();

    public AutostartReconcilerTests()
    {
        _serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
        _loggerFactory = new SerilogLoggerFactory(_serilog);

        _home = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_home);

        _settings = new SettingsStore(
            _loggerFactory.CreateLogger<SettingsStore>(),
            Path.Combine(_home, ".pisum-whisper.json"));

        _settings.Load();
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _serilog.Dispose();
        Directory.Delete(_home, true);
    }

    [Fact]
    public async Task TheRegistrationIsCreatedWhenTheSettingIsOnAndNothingIsRegistered()
    {
        Configure(true);
        _autostart.Registration = AutostartRegistration.Absent;

        await StartAsync();

        _autostart.Registration.ShouldBe(AutostartRegistration.Current);
        _autostart.Enables.ShouldBe(1);
        _autostart.Disables.ShouldBe(0);
    }

    /// <summary>
    /// The bug this reconciler shipped with, and the reason the read is three-valued. A registration
    /// naming an executable that is no longer this one agrees with an enabled setting on the only
    /// question a boolean can ask, so it was never rewritten: an install over a build a developer had
    /// been running went on launching the build output at every login, and the settings window
    /// reported that start-at-login was on, which it was — for the wrong binary.
    /// </summary>
    [Fact]
    public async Task AStaleRegistrationIsRepointedAtTheRunningExecutable()
    {
        Configure(true);
        _autostart.Registration = AutostartRegistration.Stale;

        await StartAsync();

        _autostart.Registration.ShouldBe(AutostartRegistration.Current);
        _autostart.Enables.ShouldBe(1);
        _autostart.Disables.ShouldBe(0);
    }

    /// <summary>
    /// Repointing is one write, not a delete and a create: both native implementations overwrite.
    /// The log says which of the two happened, because a registration that had to be corrected is
    /// not the same event as one that was created.
    /// </summary>
    [Fact]
    public async Task RepointingIsReportedAsSomethingOtherThanEnabling()
    {
        Configure(true);
        _autostart.Registration = AutostartRegistration.Stale;

        await StartAsync();

        _autostart.Writes.ShouldBe(1);
        LogMessages().ShouldContain(message => message.Contains("repointed at this executable", StringComparison.Ordinal));
    }

    /// <summary>A stale registration is still a registration, so turning the setting off removes it.</summary>
    [Fact]
    public async Task AStaleRegistrationIsRemovedWhenTheSettingIsOff()
    {
        Configure(false);
        _autostart.Registration = AutostartRegistration.Stale;

        await StartAsync();

        _autostart.Registration.ShouldBe(AutostartRegistration.Absent);
        _autostart.Disables.ShouldBe(1);
        _autostart.Enables.ShouldBe(0);
    }

    [Fact]
    public async Task TheRegistrationIsRemovedWhenTheSettingIsOffAndOneExists()
    {
        Configure(false);
        _autostart.Registration = AutostartRegistration.Current;

        await StartAsync();

        _autostart.Registration.ShouldBe(AutostartRegistration.Absent);
        _autostart.Disables.ShouldBe(1);
        _autostart.Enables.ShouldBe(0);
    }

    /// <summary>
    /// The reason the comparison happens first. A reconciler that wrote unconditionally would mutate
    /// the registry on every save, and log a change that did not happen — which is the mistake
    /// <c>GlobalHotkeyService.OnSettingsChanged</c> already makes with a rebind that early-returned.
    /// </summary>
    [Theory]
    [InlineData(true, AutostartRegistration.Current)]
    [InlineData(false, AutostartRegistration.Absent)]
    public async Task NothingIsWrittenWhenTheSettingAndTheRegistrationAgree(
        bool enabled,
        AutostartRegistration registration)
    {
        Configure(enabled);
        _autostart.Registration = registration;

        await StartAsync();

        _autostart.Reads.ShouldBe(1);
        _autostart.Writes.ShouldBe(0);
        LogMessages().ShouldNotContain(message => message.Contains("Start at login", StringComparison.Ordinal));
    }

    /// <summary>
    /// The user turns the switch in the settings window, and the machine agrees without a restart.
    /// </summary>
    [Fact]
    public async Task TheRegistrationIsReconciledAgainWhenSettingsAreSaved()
    {
        Configure(false);
        _autostart.Registration = AutostartRegistration.Absent;

        await StartAsync();
        _autostart.Writes.ShouldBe(0);

        Configure(true);

        _autostart.Registration.ShouldBe(AutostartRegistration.Current);
        _autostart.Enables.ShouldBe(1);
    }

    /// <summary>
    /// A restart with the setting still on restores a registration something else removed. This is
    /// what reconciling buys over calling Enable from the General tab's switch.
    /// </summary>
    [Fact]
    public async Task ARegistrationRemovedElsewhereIsRestoredOnTheNextStart()
    {
        Configure(true);
        _autostart.Registration = AutostartRegistration.Current;

        await StartAsync();
        _autostart.Writes.ShouldBe(0);

        // Something else removed it while the application was not running.
        _autostart.Registration = AutostartRegistration.Absent;

        await StartAsync();

        _autostart.Registration.ShouldBe(AutostartRegistration.Current);
        _autostart.Enables.ShouldBe(1);
    }

    /// <summary>
    /// A machine policy, a locked registry key or an unwritable home directory is a reason to lose
    /// autostart, not a reason to lose the dictation hotkey.
    /// </summary>
    [Fact]
    public async Task AFailureIsLoggedAndTheApplicationStillStarts()
    {
        Configure(true);
        _autostart.Failure = new AutostartException("the key is locked by policy");

        await Should.NotThrowAsync(StartAsync);

        _sink.WaitForMessageContaining("could not be brought to").ShouldBeTrue();
    }

    /// <summary>Once the application is stopped, a later save is nobody's business here.</summary>
    [Fact]
    public async Task StoppingUnsubscribes()
    {
        Configure(true);
        _autostart.Registration = AutostartRegistration.Current;

        var reconciler = Reconciler();
        await reconciler.StartAsync(CancellationToken.None);
        await reconciler.StopAsync(CancellationToken.None);

        Configure(false);

        _autostart.Writes.ShouldBe(0);
    }

    private AutostartReconciler Reconciler()
    {
        return new AutostartReconciler(
            _loggerFactory.CreateLogger<AutostartReconciler>(),
            _settings,
            _autostart);
    }

    /// <summary>Starts a reconciler and leaves it subscribed, which is what the running application does.</summary>
    private async Task StartAsync()
    {
        await Reconciler().StartAsync(CancellationToken.None);
    }

    private void Configure(bool startWithSystem)
    {
        var settings = _settings.CloneCurrent();
        settings.StartWithSystem = startWithSystem;
        _settings.Save(settings);
    }

    private IReadOnlyList<string> LogMessages()
    {
        return _sink.Messages;
    }
}
