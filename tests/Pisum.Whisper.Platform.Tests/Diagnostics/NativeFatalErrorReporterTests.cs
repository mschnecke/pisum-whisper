namespace Pisum.Whisper.Platform.Tests.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisum.Whisper.Core.Diagnostics;
using Pisum.Whisper.Platform.Autostart;
using Pisum.Whisper.Platform.Diagnostics;
using Pisum.Whisper.Platform.Output;
using Pisum.Whisper.Platform.Shell;
using Shouldly;

/// <summary>
/// Task 2.3 — which reporter is constructed, in the manner of <see cref="Shell.NativeShellRegistrationTests"/>.
/// </summary>
/// <remarks>
/// <b>Nothing here calls <c>Report</c>.</b> <c>MessageBoxW</c> blocks until it is dismissed, so a
/// test that showed the dialog would hang the run with no timeout — the run would be waiting on a
/// person. The dialog itself is verified by hand in tasks 7.1 and 7.2.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class NativeFatalErrorReporterTests
{
    [Fact]
    public void CreateReturnsTheImplementationForTheRunningPlatform()
    {
        var reporter = NativeFatalErrorReporter.Create();

        if (OperatingSystem.IsWindows())
        {
            reporter.ShouldBeOfType<WindowsFatalErrorReporter>();
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            reporter.ShouldBeOfType<MacOsFatalErrorReporter>();
            return;
        }

        // Neither shipped platform is a no-op rather than the PlatformNotSupportedException the
        // other native registrations throw: this is the thing that reports startup failing, so it
        // must not be a startup failure itself.
        reporter.ShouldBeOfType<NativeFatalErrorReporter.SilentFatalErrorReporter>();
    }
}

/// <summary>
/// Task 2.3 — that the reporter is <b>not</b> in the container, and stays out of it.
/// </summary>
/// <remarks>
/// The omission is the design, not an oversight: one of the reporter's four call sites is
/// <c>builder.Build()</c> failing, so a reporter resolved from the container is a reporter that does
/// not exist exactly when it is needed. A later change that "fixes" the omission fails here, with
/// this comment attached.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class NativeFatalErrorReporterRegistrationTests
{
    [Fact]
    public void NoFatalErrorReporterIsRegistered()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        // Every native registration this project has. None of them may bring a reporter with it.
        builder.Services.AddNativeOutput();
        builder.Services.AddNativeAutostart();
        builder.Services.AddNativeShell();

        using var host = builder.Build();

        host.Services.GetService<IFatalErrorReporter>()
            .ShouldBeNull("the reporter is constructed by Program, because one of its call sites is this container failing to build");
    }
}
