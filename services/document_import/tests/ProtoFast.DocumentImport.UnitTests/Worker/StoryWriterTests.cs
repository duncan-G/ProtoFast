using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;
using ProtoFast.DocumentImport.Worker.Import;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Worker;

public class StoryWriterTests
{
    [Fact]
    public void Labels_beyond_the_defaults_are_saved_to_the_story_vocabulary()
    {
        var draft = StoryJson.Deserialize<StoryDraft>("""
            {
              "title": "The Quiet Year",
              "characters": [
                { "name": "Mara", "kind": "human" },
                { "name": "Orla", "kind": "ghost" },
                { "name": "Zed", "kind": "Alien" }
              ],
              "locations": [{ "name": "Kitchen", "setting": "Interior" }],
              "containers": [{
                "label": "Act I",
                "scenes": [{
                  "title": "Morning",
                  "elements": [
                    { "type": "Heading", "location": "Kitchen", "timeOfDay": "morning" },
                    { "type": "Dialogue", "speaker": "Orla", "text": "Boo." },
                    { "type": "Transition", "transition": "Wipe to:" },
                    { "type": "Transition", "transition": "cut to:" },
                    { "type": "Transition", "transition": "IRIS OUT" }
                  ]
                }]
              }],
              "vocabulary": {
                "transitions": ["WIPE TO", "Smash Cut To"],
                "characterKinds": [{ "label": "Ghost", "avatarShape": "star" }, { "label": "Spirit", "avatarShape": "nonsense" }]
              }
            }
            """);

        var story = StoryWriter.Build(draft);

        Assert.Equal(["MORNING"], story.Vocabulary.TimesOfDay);
        Assert.Equal(["WIPE TO", "IRIS OUT"], story.Vocabulary.Transitions);
        Assert.Equal(
            [("Ghost", AvatarShape.Star), ("Spirit", AvatarShape.Circle), ("Alien", AvatarShape.Circle)],
            story.Vocabulary.CharacterKinds.Select(k => (k.Label, k.AvatarShape)));
        Assert.Equal(["Human", "Ghost", "Alien"], story.Characters.Select(c => c.Kind));

        var elements = story.Containers[0].Scenes[0].Elements;
        Assert.Equal("MORNING", elements[0].TimeOfDay);
        Assert.Equal(["WIPE TO", "CUT TO", "IRIS OUT"], elements.Where(e => e.Type == SceneElementType.Transition).Select(e => e.Transition));
    }
}
