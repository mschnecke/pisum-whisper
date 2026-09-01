namespace Pisum.Whisper.App.Tests.ViewModels;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.App.Settings.Views;
using Pisum.Whisper.App.Tests.Settings;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>Task 4.4 — the Providers tab's list operations.</summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class ProvidersViewModelTests : SettingsEditorTestBase
{
    private readonly IGeminiKeyProbe _probe = A.Fake<IGeminiKeyProbe>();

    private ProvidersViewModel NewViewModel(SettingsEditor editor)
    {
        return new ProvidersViewModel(
            editor, _probe, new ModelListCache(NullLogger<ModelListCache>.Instance), Store.Current);
    }

    private void SeedOneEntry(string id = "one", string apiKey = "first")
    {
        var settings = Store.CloneCurrent();
        settings.Providers.Add(new ProviderConfig {Id = id, ApiKey = apiKey});
        Store.Save(settings);
    }

    [Fact]
    public void TheConfiguredEntriesAreListed()
    {
        SeedOneEntry();
        SeedOneEntry("two", "second");

        var viewModel = NewViewModel(NewEditor());

        viewModel.Entries.Select(entry => entry.Id).ShouldBe(["one", "two"]);
        viewModel.Entries[0].ApiKey.ShouldBe("first");
        viewModel.Entries[0].IsKeyRevealed.ShouldBeFalse();
    }

    [Fact]
    public async Task Add_AppendsAnEnabledEntryWithANonEmptyUniqueId()
    {
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        viewModel.AddCommand.Execute(null);
        viewModel.AddCommand.Execute(null);
        await editor.FlushAsync();

        var ids = Store.Current.Providers.Select(entry => entry.Id).ToList();
        ids.Count.ShouldBe(2);
        ids.ShouldAllBe(id => id.Length > 0);
        ids.Distinct().Count().ShouldBe(2);
        Store.Current.Providers.ShouldAllBe(entry => entry.Enabled);
        viewModel.Entries.Select(entry => entry.Id).ShouldBe(ids);
    }

    [Fact]
    public async Task Remove_TakesTheEntryOutOfTheDraftAndTheList()
    {
        SeedOneEntry();
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        viewModel.RemoveCommand.Execute(viewModel.Entries[0]);
        await editor.FlushAsync();

        Store.Current.Providers.ShouldBeEmpty();
        viewModel.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheEnableToggle_ReachesTheDraft()
    {
        SeedOneEntry();
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        viewModel.Entries[0].Enabled = false;
        await editor.FlushAsync();

        Store.Current.Providers.Single().Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task TypingAKey_ReachesTheDraft()
    {
        SeedOneEntry(apiKey: string.Empty);
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        viewModel.Entries[0].ApiKey = "AIza-typed";
        await editor.FlushAsync();

        Store.Current.Providers.Single().ApiKey.ShouldBe("AIza-typed");
    }

    [Fact]
    public void TheRevealToggle_IsOffUntilTheUserAsks()
    {
        SeedOneEntry();
        var viewModel = NewViewModel(NewEditor());

        viewModel.Entries[0].IsKeyRevealed.ShouldBeFalse();

        viewModel.Entries[0].IsKeyRevealed = true;
        viewModel.Entries[0].IsKeyRevealed.ShouldBeTrue();

        viewModel.Entries[0].IsKeyRevealed = false;
        viewModel.Entries[0].IsKeyRevealed.ShouldBeFalse();
    }

    [Fact]
    public async Task ASecondEditOfTheSameEntryAfterACommit_IsStillPersisted()
    {
        // The test that fails if an entry view model closed over its ProviderConfig: the commit
        // replaces the graph, so the second edit would land in one nothing will ever save.
        SeedOneEntry(apiKey: string.Empty);
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        viewModel.Entries[0].ApiKey = "first";
        await editor.FlushAsync();

        viewModel.Entries[0].ApiKey = "second";
        viewModel.Entries[0].Enabled = false;
        await editor.FlushAsync();

        var entry = Store.Current.Providers.Single();
        entry.ApiKey.ShouldBe("second");
        entry.Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task AnEntryAddedInThisSession_IsStillEditableAfterACommit()
    {
        var editor = NewEditor();
        var viewModel = NewViewModel(editor);

        viewModel.AddCommand.Execute(null);
        await editor.FlushAsync();

        viewModel.Entries[0].ApiKey = "typed after the add committed";
        await editor.FlushAsync();

        Store.Current.Providers.Single().ApiKey.ShouldBe("typed after the add committed");
    }

    [Fact]
    public void ConstructingTheViewModel_WritesNothing()
    {
        SeedOneEntry();
        var before = Saves;

        _ = NewViewModel(NewEditor());

        Saves.ShouldBe(before);
    }

    [AvaloniaFact]
    public void TheViewLoadsAndBinds()
    {
        SeedOneEntry();
        var viewModel = NewViewModel(NewEditor());
        var window = new Window {Content = new ProvidersView {DataContext = viewModel}};

        window.Show();

        var keyBox = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "ApiKeyBox");
        keyBox.Text.ShouldBe("first");
        keyBox.RevealPassword.ShouldBeFalse();

        window.GetVisualDescendants().OfType<Button>()
            .ShouldContain(button => Equals(button.Content, "Add provider"));
    }
}
