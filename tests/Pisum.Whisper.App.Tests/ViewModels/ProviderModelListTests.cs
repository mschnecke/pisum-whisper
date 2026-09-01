namespace Pisum.Whisper.App.Tests.ViewModels;

using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Whisper.App.Settings;
using Pisum.Whisper.App.Settings.ViewModels;
using Pisum.Whisper.App.Tests.Settings;
using Pisum.Whisper.Core.Settings;
using Pisum.Whisper.Core.Transcription;
using Shouldly;

/// <summary>Task 4.5 — the model dropdown, its per-key cache and Refresh.</summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class ProviderModelListTests : SettingsEditorTestBase
{
    private readonly IGeminiKeyProbe _probe = A.Fake<IGeminiKeyProbe>();

    private readonly ModelListCache _cache = new(NullLogger<ModelListCache>.Instance);

    private static readonly GeminiModel Flash = new("gemini-2.5-flash", "Gemini 2.5 Flash");

    private static readonly GeminiModel Pro = new("gemini-2.5-pro", "Gemini 2.5 Pro");

    private ProviderEntryViewModel NewEntry(SettingsEditor editor,
                                            string id = "one",
                                            string apiKey = "AIza-one",
                                            string? model = null)
    {
        return new ProviderEntryViewModel(editor, _probe, _cache, id, apiKey, model, true);
    }

    private void Listing(string apiKey, params GeminiModel[] models)
    {
        A.CallTo(() => _probe.ListModelsAsync(apiKey, A<CancellationToken>._))
            .Returns(models);
    }

    [Fact]
    public void TheDefaultOption_NamesTheModelTheProviderFallsBackTo()
    {
        ProviderEntryViewModel.DefaultModelOption.Id.ShouldBeEmpty();
        ProviderEntryViewModel.DefaultModelOption.DisplayName.ShouldBe($"Default ({GeminiDefaults.Model})");
    }

    [Fact]
    public void AnEntryWithNoModel_StartsOnTheDefaultOption()
    {
        var entry = NewEntry(NewEditor());

        entry.SelectedModel.ShouldBe(ProviderEntryViewModel.DefaultModelOption);
        entry.Models.ShouldContain(ProviderEntryViewModel.DefaultModelOption);
    }

    [Fact]
    public async Task ListingOffersTheDefaultOptionAndTheFetchedModels()
    {
        Listing("AIza-one", Flash, Pro);
        var entry = NewEntry(NewEditor());

        await entry.LoadModelsCommand.ExecuteAsync(null);

        entry.Models.Select(model => model.Id)
            .ShouldBe([string.Empty, "gemini-2.5-flash", "gemini-2.5-pro"]);
    }

    [Fact]
    public async Task ASecondViewOfTheSameKey_DoesNotRefetch()
    {
        Listing("AIza-one", Flash);
        var editor = NewEditor();

        await NewEntry(editor).LoadModelsCommand.ExecuteAsync(null);
        await NewEntry(editor, "two").LoadModelsCommand.ExecuteAsync(null);

        A.CallTo(() => _probe.ListModelsAsync("AIza-one", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ADistinctKey_IsFetchedOnItsOwn()
    {
        Listing("AIza-one", Flash);
        Listing("AIza-two", Pro);
        var editor = NewEditor();

        await NewEntry(editor).LoadModelsCommand.ExecuteAsync(null);
        await NewEntry(editor, "two", "AIza-two").LoadModelsCommand.ExecuteAsync(null);

        A.CallTo(() => _probe.ListModelsAsync("AIza-one", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _probe.ListModelsAsync("AIza-two", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Refresh_FetchesAgain()
    {
        Listing("AIza-one", Flash);
        var entry = NewEntry(NewEditor());

        await entry.LoadModelsCommand.ExecuteAsync(null);
        await entry.RefreshModelsCommand.ExecuteAsync(null);

        A.CallTo(() => _probe.ListModelsAsync("AIza-one", A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task AFailedListing_LeavesTheDropdownUsableRatherThanThrowing()
    {
        A.CallTo(() => _probe.ListModelsAsync(A<string>._, A<CancellationToken>._))
            .Throws(new TranscriptionException("The key was rejected.", ErrorCategory.Authentication));

        var entry = NewEntry(NewEditor());

        await Should.NotThrowAsync(entry.LoadModelsCommand.ExecuteAsync(null));

        entry.Models.ShouldContain(ProviderEntryViewModel.DefaultModelOption);
        entry.SelectedModel.ShouldBe(ProviderEntryViewModel.DefaultModelOption);
    }

    [Fact]
    public async Task AFailedListing_IsNotCachedSoRefreshTriesAgain()
    {
        A.CallTo(() => _probe.ListModelsAsync(A<string>._, A<CancellationToken>._))
            .Throws(new TranscriptionException("Gemini could not be reached.", ErrorCategory.Network));

        var entry = NewEntry(NewEditor());

        await entry.LoadModelsCommand.ExecuteAsync(null);
        await entry.LoadModelsCommand.ExecuteAsync(null);

        A.CallTo(() => _probe.ListModelsAsync("AIza-one", A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public void ListingAndRefreshAreUnavailableWhileTheKeyIsEmpty()
    {
        var entry = NewEntry(NewEditor(), apiKey: string.Empty);

        entry.HasApiKey.ShouldBeFalse();
        entry.LoadModelsCommand.CanExecute(null).ShouldBeFalse();
        entry.RefreshModelsCommand.CanExecute(null).ShouldBeFalse();

        entry.ApiKey = "AIza-one";

        entry.HasApiKey.ShouldBeTrue();
        entry.LoadModelsCommand.CanExecute(null).ShouldBeTrue();
        entry.RefreshModelsCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task ChoosingAModel_ReachesTheDraft()
    {
        var settings = Store.CloneCurrent();
        settings.Providers.Add(new ProviderConfig {Id = "one", ApiKey = "AIza-one"});
        Store.Save(settings);

        Listing("AIza-one", Flash, Pro);
        var editor = NewEditor();
        var entry = NewEntry(editor);
        await entry.LoadModelsCommand.ExecuteAsync(null);

        entry.SelectedModel = entry.Models.Single(model => model.Id == "gemini-2.5-pro");
        await editor.FlushAsync();

        Store.Current.Providers.Single().Model.ShouldBe("gemini-2.5-pro");
    }

    [Fact]
    public async Task AConfiguredModelTheListingDoesNotOffer_IsKeptRatherThanSilentlyReset()
    {
        Listing("AIza-one", Flash);
        var entry = NewEntry(NewEditor(), model: "gemini-1.0-retired");

        await entry.LoadModelsCommand.ExecuteAsync(null);

        entry.SelectedModel.Id.ShouldBe("gemini-1.0-retired");
        entry.Models.Select(model => model.Id).ShouldContain("gemini-1.0-retired");
    }
}
