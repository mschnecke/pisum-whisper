namespace Pisum.Whisper.Platform.Tests.Autostart;

using System.Xml.Linq;
using Pisum.Whisper.Core.Autostart;
using Pisum.Whisper.Platform.Autostart;
using Shouldly;

/// <summary>
/// Task 4.3 — the macOS login agent, round-tripped against a real directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>These run on any operating system.</b> <c>MacOsAutostart</c> writes a plist into an injected
/// directory and calls nothing macOS-specific — no <c>launchctl</c>, no <c>SMAppService</c>, no
/// AppKit — so the file's shape and the enable/disable/re-enable cycle are covered from Windows, and
/// only the effect on logging in needs Apple hardware.
/// </para>
/// <para>
/// The <c>CA1416</c> suppressions below are the price of that: the type carries
/// <c>[SupportedOSPlatform("macos")]</c> because it is macOS's mechanism, while its implementation
/// is portable enough to test anywhere.
/// </para>
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class MacOsAutostartTests : IDisposable
{
    private readonly string _directory;

    public MacOsAutostartTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void EnablingWritesTheAgentIntoTheLaunchAgentsDirectory()
    {
        var autostart = Create();

        autostart.IsEnabled().ShouldBeFalse();

        autostart.Enable();

        autostart.IsEnabled().ShouldBeTrue();
        File.Exists(PlistPath()).ShouldBeTrue();
    }

    /// <summary>
    /// The three keys <c>launchd</c> reads. Asserted through a parser rather than by substring, so a
    /// plist it would refuse fails here rather than at someone's next login.
    /// </summary>
    [Fact]
    public void TheAgentParsesAndCarriesAllThreeKeys()
    {
        Create().Enable();

        var document = XDocument.Load(PlistPath());
        var dictionary = document.Root!.Element("dict")!;
        var children = dictionary.Elements().ToList();

        Value(children, "Label").Value.ShouldBe("net.pisum.whisper");
        Value(children, "ProgramArguments").Elements("string").Single().Value
            .ShouldBe(Environment.ProcessPath);
        Value(children, "RunAtLoad").Name.LocalName.ShouldBe("true");
    }

    [Fact]
    public void EnablingTwiceLeavesOneAgent()
    {
        var autostart = Create();

        autostart.Enable();
        autostart.Enable();

        Directory.GetFiles(_directory).Length.ShouldBe(1);
        autostart.IsEnabled().ShouldBeTrue();
    }

    [Fact]
    public void DisablingRemovesTheAgent()
    {
        var autostart = Create();

        autostart.Enable();
        autostart.Disable();

        autostart.IsEnabled().ShouldBeFalse();
        File.Exists(PlistPath()).ShouldBeFalse();
    }

    [Fact]
    public void DisablingWhenNotRegisteredDoesNothing()
    {
        var autostart = Create();

        Should.NotThrow(autostart.Disable);

        autostart.IsEnabled().ShouldBeFalse();
    }

    [Fact]
    public void ReEnablingAfterADisableWritesItBack()
    {
        var autostart = Create();

        autostart.Enable();
        autostart.Disable();
        autostart.Enable();

        autostart.IsEnabled().ShouldBeTrue();
        Directory.GetFiles(_directory).Length.ShouldBe(1);
    }

    private IAutostartService Create()
    {
#pragma warning disable CA1416
        return new MacOsAutostart(_directory);
#pragma warning restore CA1416
    }

    private string PlistPath()
    {
        return Path.Combine(_directory, "net.pisum.whisper.plist");
    }

    /// <summary>The element after the <c>&lt;key&gt;</c> named <paramref name="key"/>, as a plist pairs them.</summary>
    private static XElement Value(List<XElement> children, string key)
    {
        var index = children.FindIndex(child => child.Name.LocalName == "key" && child.Value == key);

        index.ShouldBeGreaterThanOrEqualTo(0, $"the plist should carry a '{key}' key");

        return children[index + 1];
    }
}
