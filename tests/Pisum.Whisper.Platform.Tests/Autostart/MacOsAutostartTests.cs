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

        autostart.Read().ShouldBe(AutostartRegistration.Absent);

        autostart.Enable();

        autostart.Read().ShouldBe(AutostartRegistration.Current);
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

        var children = Children();

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
        autostart.Read().ShouldBe(AutostartRegistration.Current);
    }

    /// <summary>
    /// An agent naming something else — the install that replaced the build a developer was running,
    /// or an application moved — is <c>Stale</c>, not <c>Absent</c> and emphatically not <c>Current</c>.
    /// Reading it as merely "registered" is what let the machine go on launching the old path at
    /// every login while the setting truthfully reported that start-at-login was on.
    /// </summary>
    [Fact]
    public void AnAgentNamingAnotherExecutableIsStaleAndIsRepointedByEnabling()
    {
        var autostart = Create();
        Directory.CreateDirectory(_directory);

        File.WriteAllText(PlistPath(), Agent("/Applications/Somewhere Else.app/Contents/MacOS/Whisper"));

        autostart.Read().ShouldBe(AutostartRegistration.Stale);

        autostart.Enable();

        autostart.Read().ShouldBe(AutostartRegistration.Current);
        Value(Children(), "ProgramArguments").Elements("string").Single().Value
            .ShouldBe(Environment.ProcessPath);
        Directory.GetFiles(_directory).Length.ShouldBe(1);
    }

    /// <summary>
    /// A file that is not a plist at all reads the same way, because the question is whether what is
    /// on disk is what <c>Enable</c> would write — not whether it parses.
    /// </summary>
    [Fact]
    public void AnAgentThatIsNotEvenAPlistIsStale()
    {
        var autostart = Create();
        Directory.CreateDirectory(_directory);

        File.WriteAllText(PlistPath(), "this is not a plist");

        autostart.Read().ShouldBe(AutostartRegistration.Stale);
    }

    [Fact]
    public void DisablingRemovesTheAgent()
    {
        var autostart = Create();

        autostart.Enable();
        autostart.Disable();

        autostart.Read().ShouldBe(AutostartRegistration.Absent);
        File.Exists(PlistPath()).ShouldBeFalse();
    }

    [Fact]
    public void DisablingWhenNotRegisteredDoesNothing()
    {
        var autostart = Create();

        Should.NotThrow(autostart.Disable);

        autostart.Read().ShouldBe(AutostartRegistration.Absent);
    }

    [Fact]
    public void ReEnablingAfterADisableWritesItBack()
    {
        var autostart = Create();

        autostart.Enable();
        autostart.Disable();
        autostart.Enable();

        autostart.Read().ShouldBe(AutostartRegistration.Current);
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

    /// <summary>The written agent's <c>dict</c> children, which a plist pairs as key then value.</summary>
    private List<XElement> Children()
    {
        return XDocument.Load(PlistPath()).Root!.Element("dict")!.Elements().ToList();
    }

    /// <summary>
    /// A well-formed agent naming <paramref name="executablePath"/>, so that a stale registration is
    /// a plausible one rather than a corrupt file. It is spelled out here rather than taken from the
    /// production writer, or the test would agree with whatever that produced.
    /// </summary>
    private static string Agent(string executablePath)
    {
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                  <dict>
                    <key>Label</key>
                    <string>net.pisum.whisper</string>
                    <key>ProgramArguments</key>
                    <array>
                      <string>{executablePath}</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true />
                  </dict>
                </plist>
                """;
    }

    /// <summary>The element after the <c>&lt;key&gt;</c> named <paramref name="key"/>, as a plist pairs them.</summary>
    private static XElement Value(List<XElement> children, string key)
    {
        var index = children.FindIndex(child => child.Name.LocalName == "key" && child.Value == key);

        index.ShouldBeGreaterThanOrEqualTo(0, $"the plist should carry a '{key}' key");

        return children[index + 1];
    }
}
