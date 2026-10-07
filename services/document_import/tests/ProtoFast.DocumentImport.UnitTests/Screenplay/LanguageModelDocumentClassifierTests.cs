using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Families;
using ProtoFast.DocumentImport.Engine.InMemory;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Screenplay;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Classification;
using ProtoFast.DocumentImport.Screenplay.Models;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

public class LanguageModelDocumentClassifierTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private const string SynopsisReply = """
        {"summary": "Mara leaves the city after her brother's trial and settles in a coastal town.", "form": "novel manuscript", "language": "English", "structure": "Twelve numbered chapters of prose."}
        """;

    private const string NewPlayFamily = """
        {"new_family": {"name": "stage-play", "display_name": "Stage plays", "description": "Plays written for the stage: acts and scenes of dialogue with stage directions."}, "reason": "No family covers plays."}
        """;

    private readonly ScriptedLanguageModelFactory _models = new();
    private readonly ServiceProvider _services;

    public LanguageModelDocumentClassifierTests()
    {
        _services = Build(_models);
    }

    private static ServiceProvider Build(ScriptedLanguageModelFactory models, Action<DocumentClassifierOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentWorkflowEngine();
        services.AddInMemoryWorkflowEngineStores();
        services.AddScreenplayDiscovery(configureClassifier: configure);
        services.AddSingleton<ILanguageModelFactory>(models);
        return services.BuildServiceProvider();
    }

    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    private IDocumentClassifier Classifier => Get<IDocumentClassifier>();

    private IDocumentFamilyRegistry Families => Get<IDocumentFamilyRegistry>();

    private static Task<ArtifactRef> InputAsync(IServiceProvider services, string text) =>
        services.GetRequiredService<IArtifactStore>().PutAsync(
            Core.DocumentImportIds.New(), ArtifactRef.InputStageId, new MemoryStream(Encoding.UTF8.GetBytes(text)), StoryStages.SourceContract, Ct);

    private Task<ArtifactRef> InputAsync(string text) => InputAsync(_services, text);

    private Task RegisterAsync(string family, string description) =>
        Families.CreateAsync(new DocumentFamilyInfo(family, family, description, "op-1", Now, Now), Ct);

    private IEnumerable<string> PromptsTo(string modelClass) => _models.Calls.Where(c => c.ModelClass == modelClass).Select(c => c.User);

    [Fact]
    public async Task A_document_joins_the_registered_family_whose_description_fits_its_synopsis()
    {
        await RegisterAsync("screenplay", "Text already laid out as a screenplay.");
        await RegisterAsync("novel", "Long-form prose fiction in chapters.");
        _models.Script(ModelClasses.Small, (_, _) => SynopsisReply);
        _models.Script(ModelClasses.Medium, (_, _) => """{"family": "novel", "reason": "Chapters of prose."}""");

        var signature = await Classifier.ClassifyAsync(await InputAsync("Chapter One\n\nMara left the city."), Ct);

        Assert.Equal("novel", signature.Family);
        Assert.Equal(
            ("false", "novel manuscript", "English", "Chapters of prose.", "32"),
            (signature.Facets["family_created"], signature.Facets["form"], signature.Facets["language"], signature.Facets["reason"], signature.Facets["chars"]));
        Assert.Contains("Mara leaves the city", signature.Facets["synopsis"]);
        Assert.Contains("Chapter One", Assert.Single(PromptsTo(ModelClasses.Small)));

        // The classifier reads the synopsis and the families, never the manuscript.
        var prompt = Assert.Single(PromptsTo(ModelClasses.Medium));
        Assert.Contains("- screenplay: Text already laid out as a screenplay.", prompt);
        Assert.Contains("- novel: Long-form prose fiction in chapters.", prompt);
        Assert.Contains("Synopsis: Mara leaves the city", prompt);
        Assert.DoesNotContain("Chapter One", prompt);
    }

    [Fact]
    public async Task A_document_no_family_fits_opens_one_that_the_next_classification_is_shown()
    {
        _models.Script(ModelClasses.Small, (_, _) => SynopsisReply);
        _models.Script(
            ModelClasses.Medium,
            (_, _) => NewPlayFamily,
            (_, _) => """{"family": "stage-play", "reason": "Another play."}""");

        var first = await Classifier.ClassifyAsync(await InputAsync("ACT ONE"), Ct);
        var second = await Classifier.ClassifyAsync(await InputAsync("ACT TWO"), Ct);

        Assert.Equal(("stage-play", "true"), (first.Family, first.Facets["family_created"]));
        Assert.Equal(("stage-play", "false"), (second.Family, second.Facets["family_created"]));
        var registered = Assert.Single(await Families.ListAsync(Ct));
        Assert.Equal(
            ("stage-play", "Stage plays", LanguageModelDocumentClassifier.CreatedBy),
            (registered.Family, registered.DisplayName, registered.CreatedBy));
        Assert.StartsWith("Plays written for the stage", registered.Description);
        var prompts = PromptsTo(ModelClasses.Medium).ToList();
        Assert.Contains("None yet.", prompts[0]);
        Assert.Contains("- stage-play: Plays written for the stage", prompts[1]);
    }

    [Fact]
    public async Task A_new_family_another_import_opened_first_is_joined_as_it_is()
    {
        await RegisterAsync("stage-play", "Plays, as the operator described them.");
        _models.Script(ModelClasses.Small, (_, _) => SynopsisReply);
        _models.Script(ModelClasses.Medium, (_, _) => NewPlayFamily);

        var signature = await Classifier.ClassifyAsync(await InputAsync("ACT ONE"), Ct);

        Assert.Equal(("stage-play", "false"), (signature.Family, signature.Facets["family_created"]));
        Assert.Equal("Plays, as the operator described them.", Assert.Single(await Families.ListAsync(Ct)).Description);
    }

    [Theory]
    [InlineData("""{"family": "invoice", "reason": "Looks like an invoice."}""")]
    [InlineData("""{"new_family": {"name": "Stage Play!", "display_name": "Stage plays", "description": "Plays."}}""")]
    [InlineData("""{"new_family": {"name": "stage-play", "display_name": "Stage plays", "description": ""}}""")]
    [InlineData("""{"reason": "I could not decide."}""")]
    public async Task An_answer_that_names_no_usable_family_fails_the_classification(string reply)
    {
        await RegisterAsync("novel", "Long-form prose fiction in chapters.");
        _models.Script(ModelClasses.Small, (_, _) => SynopsisReply);
        _models.Script(ModelClasses.Medium, (_, _) => reply);

        await Assert.ThrowsAsync<DocumentClassificationException>(async () => await Classifier.ClassifyAsync(await InputAsync("x"), Ct));

        Assert.Equal(["novel"], (await Families.ListAsync(Ct)).Select(f => f.Family));
    }

    [Fact]
    public async Task A_long_manuscript_reaches_the_synopsis_model_as_its_opening_middle_and_ending()
    {
        var services = Build(_models, o => o.MaxExcerptChars = 40);
        var text = new string('a', 100) + new string('b', 100) + new string('c', 100);
        _models.Script(ModelClasses.Small, (_, _) => SynopsisReply);
        _models.Script(ModelClasses.Medium, (_, _) => NewPlayFamily);

        var signature = await services.GetRequiredService<IDocumentClassifier>().ClassifyAsync(await InputAsync(services, text), Ct);

        Assert.Equal("300", signature.Facets["chars"]);
        var prompt = Assert.Single(PromptsTo(ModelClasses.Small));
        Assert.Contains("<excerpt total-characters=\"300\">", prompt);
        Assert.Contains(new string('a', 20) + ManuscriptExcerpt.Cut + new string('b', 10) + ManuscriptExcerpt.Cut + new string('c', 10), prompt);
        Assert.DoesNotContain(new string('a', 21), prompt);
    }
}
