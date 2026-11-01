using ProtoFast.DocumentImport.Screenplay.Drafts;
using ProtoFast.DocumentImport.Worker.HistoricalStories;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.HistoricalStories;

public sealed class DraftProblemsTests
{
    private static StoryDraft Draft(params SceneElementDraft[] elements) =>
        new("The Ride", [new CharacterDraft("Paul Revere", "Human", null)], [new LocationDraft("NORTH CHURCH", "Exterior", null)], [],
            [new ContainerDraft("Act I", [new SceneDraft("Lanterns", elements)])]);

    private static SceneElementDraft Heading => new("Heading", null, "NORTH CHURCH", "NIGHT", null, null, null);

    private static SceneElementDraft Narration => new("Narration", "In April 1775, Boston was an occupied town.", null, null, null, null, null);

    [Fact]
    public void A_narrated_documentary_of_headed_scenes_has_no_problems() =>
        Assert.Empty(DraftProblems.Find(Draft(
            Heading,
            Narration,
            new SceneElementDraft("Action", "Two lanterns flare in the steeple.", null, null, null, null, null),
            new SceneElementDraft("Dialogue", "Two, then.", null, null, "Paul Revere", null, null),
            new SceneElementDraft("Transition", null, null, null, null, null, "CUT TO"))));

    [Fact]
    public void An_unheaded_unnarrated_scene_and_a_speakerless_line_are_problems()
    {
        var problems = DraftProblems.Find(Draft(
            new SceneElementDraft("Description", "The steeple is tall.", null, null, null, null, null),
            new SceneElementDraft("Dialogue", "Go!", null, null, null, null, null)));

        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.Contains("does not open with a Heading"));
        Assert.Contains(problems, p => p.Contains("has no narration"));
        Assert.Contains(problems, p => p.Contains("type Description is not allowed"));
        Assert.Contains(problems, p => p.Contains("Dialogue needs a speaker"));
    }

    [Fact]
    public void A_narrator_character_is_a_problem() =>
        Assert.Contains(DraftProblems.Find(Draft(
                Heading,
                Narration,
                new SceneElementDraft("Dialogue", "The regulars are out.", null, null, "Narrator", "V.O.", null))),
            p => p.Contains("The narrator is a character"));
}
