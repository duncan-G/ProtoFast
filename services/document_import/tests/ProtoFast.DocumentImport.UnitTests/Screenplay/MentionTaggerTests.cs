using Microsoft.Extensions.Logging.Abstractions;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;
using ProtoFast.DocumentImport.Screenplay.Tagging;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

public class MentionTaggerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ScriptedLanguageModelFactory _models = new();

    private MentionTagger Tagger => new(_models, new MentionTaggerOptions(), NullLogger<MentionTagger>.Instance);

    private static StoryDraft Story(string library, params string[] texts) =>
        StoryJson.Deserialize<StoryDraft>($$"""
            {
              "title": "The Quiet Year",
              {{library}},
              "containers": [{
                "label": "Act I",
                "scenes": [{
                  "title": "Morning",
                  "elements": [
                    { "type": "Heading", "location": "Kitchen", "timeOfDay": "DAY" },
                    {{string.Join(",\n", texts.Select(t => $$"""{ "type": "Action", "text": {{System.Text.Json.JsonSerializer.Serialize(t)}} }"""))}}
                  ]
                }]
              }]
            }
            """);

    private static IReadOnlyList<SceneElementDraft> Elements(StoryDraft story) => story.Containers[0].Scenes[0].Elements;

    private static void AssertMentionsMatchText(SceneElementDraft element)
    {
        foreach (var mention in element.Mentions!)
        {
            Assert.Equal(
                "@" + mention.Name,
                element.Text!.Substring(mention.Offset, mention.Length),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Characters_and_locations_are_tagged_where_capitalised_with_the_longest_name_winning()
    {
        var story = Story(
            """
            "characters": [{ "name": "Mara" }, { "name": "Mara Voss" }, { "name": "Hope" }],
            "locations": [{ "name": "Harbor Docks", "setting": "Exterior" }, { "name": "Kitchen", "setting": "Interior" }]
            """,
            "  Mara Voss walks to the docks. MARA waves.  ",
            "Hope cheers the Marathon runners at Harbor Docks, and Mara.");

        var tagged = await Tagger.TagAsync(story, Ct);

        var elements = Elements(tagged);
        Assert.Equal(Elements(story)[0], elements[0]);
        Assert.Equal("@Mara Voss walks to the docks. @MARA waves.", elements[1].Text);
        Assert.Equal("@Hope cheers the Marathon runners at @Harbor Docks, and @Mara.", elements[2].Text);
        Assert.Equal(
            [(MentionKind.Character, "Mara Voss"), (MentionKind.Character, "Mara")],
            elements[1].Mentions!.Select(m => (m.Kind, m.Name)));
        Assert.Equal(
            [(MentionKind.Character, "Hope"), (MentionKind.Location, "Harbor Docks"), (MentionKind.Character, "Mara")],
            elements[2].Mentions!.Select(m => (m.Kind, m.Name)));
        AssertMentionsMatchText(elements[1]);
        AssertMentionsMatchText(elements[2]);
        Assert.Empty(_models.Calls);
    }

    [Fact]
    public async Task Multi_word_props_are_tagged_in_any_case_and_one_word_props_when_the_model_confirms_them()
    {
        _models.Script(ModelClasses.Small, (_, _) => """{"references": [1]}""");
        var story = Story(
            """
            "props": [{ "name": "Brass Key", "description": "Opens the lighthouse." }, { "name": "Map", "description": "Her father's chart." }]
            """,
            "She pockets the brass key and unrolls the map.",
            "Time to map it out.");

        var tagged = await Tagger.TagAsync(story, Ct);

        var elements = Elements(tagged);
        Assert.Equal("She pockets the @brass key and unrolls the @map.", elements[1].Text);
        Assert.Equal("Time to map it out.", elements[2].Text);
        Assert.Empty(elements[2].Mentions!);
        AssertMentionsMatchText(elements[1]);

        var (modelClass, _, user) = Assert.Single(_models.Calls);
        Assert.Equal(ModelClasses.Small, modelClass);
        Assert.Contains("1. Map (prop): She pockets the brass key and unrolls the [[map]].", user);
        Assert.Contains("2. Map (prop): Time to [[map]] it out.", user);
        Assert.DoesNotContain("Brass Key", user);
    }

    [Fact]
    public async Task A_name_the_manuscript_also_writes_in_lowercase_is_tagged_only_where_the_model_confirms_it()
    {
        _models.Script(ModelClasses.Small, (_, _) => """{"references": [2]}""");
        var story = Story(
            """
            "characters": [{ "name": "Will", "description": "The keeper's son." }, { "name": "Mara" }]
            """,
            "Will you hold this, Mara? Will asks.",
            "She will not.");

        var tagged = await Tagger.TagAsync(story, Ct);

        var elements = Elements(tagged);
        Assert.Equal("Will you hold this, @Mara? @Will asks.", elements[1].Text);
        Assert.Equal("She will not.", elements[2].Text);
        AssertMentionsMatchText(elements[1]);

        var (_, _, user) = Assert.Single(_models.Calls);
        Assert.Contains("- Will (character): The keeper's son.", user);
        Assert.Contains("1. Will (character): [[Will]] you hold", user);
        Assert.Contains("2. Will (character): Will you hold this, Mara? [[Will]] asks.", user);
        Assert.DoesNotContain("[[will]]", user);
        Assert.DoesNotContain("[[Mara]]", user);
    }

    [Fact]
    public async Task A_failed_confirmation_leaves_one_word_props_untagged_and_keeps_the_rest()
    {
        _models.Script(ModelClasses.Small, (_, _) => "I cannot tell.");
        var story = Story(
            """
            "characters": [{ "name": "Mara" }],
            "props": [{ "name": "Map" }]
            """,
            "Mara unrolls the map.");

        var tagged = await Tagger.TagAsync(story, Ct);

        Assert.Equal("@Mara unrolls the map.", Elements(tagged)[1].Text);
    }
}
