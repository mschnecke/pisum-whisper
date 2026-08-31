namespace Pisum.Whisper.Platform.Tests;

using Xunit.v3;

/// <summary>
/// The category every test class carries. <c>Xunit.TraitAttribute</c> is sealed, so this implements
/// <see cref="ITraitAttribute"/> directly rather than deriving from it; the runner sees the same
/// <c>Category</c> trait either way, so <c>--filter-trait Category=Unit</c> and
/// <c>--filter-not-trait Category=Manual</c> work as they would against the string form.
/// </summary>
/// <remarks>
/// The value is decided by what the test touches, not by where it lives, and the rule is mechanical:
/// if the class constructor or any constructor up its base chain reaches <c>Path.GetTempPath</c>,
/// <c>Directory.CreateDirectory</c>, <c>File.WriteAll*</c>, <c>new ServiceCollection</c> or
/// <c>Host.CreateApplicationBuilder</c>, it is an integration test. Three bases put their derived
/// classes on that side by themselves; <c>TextOutputTestBase</c> is the one that does not.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public abstract class TestCategoryAttribute(string category) : Attribute, ITraitAttribute
{
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits()
    {
        return [new KeyValuePair<string, string>(Traits.Category, category)];
    }
}

/// <summary>In-memory objects and fakes only — no filesystem, no container, no network.</summary>
public sealed class UnitTestAttribute() : TestCategoryAttribute(Traits.Categories.Unit);

/// <summary>Writes real files under the temp path, or wires a real container or generic host.</summary>
public sealed class IntegrationTestAttribute() : TestCategoryAttribute(Traits.Categories.Integration);

/// <summary>Needs a microphone, a desktop session, or a real API key; gated by <see cref="ManualTests"/>.</summary>
public sealed class ManualTestAttribute() : TestCategoryAttribute(Traits.Categories.Manual);
