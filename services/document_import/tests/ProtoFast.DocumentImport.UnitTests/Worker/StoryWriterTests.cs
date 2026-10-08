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

    [Fact]
    public void A_heading_or_speaker_spelled_with_other_quotes_reuses_the_declared_entry()
    {
        var draft = StoryJson.Deserialize<StoryDraft>("""
            {
              "title": "In the Name of the Mother",
              "characters": [{ "name": "D'ARCY", "kind": "Human" }],
              "locations": [{ "name": "KING'S ROAD, CROWNLANDS", "setting": "Exterior" }],
              "containers": [{ "label": "Act I", "scenes": [{ "title": "Road", "elements": [
                { "type": "Heading", "location": "KING’S ROAD, CROWNLANDS" },
                { "type": "Dialogue", "speaker": "D’ARCY", "text": "Ride on." }
              ] }] }]
            }
            """);

        var story = StoryWriter.Build(draft);

        var location = Assert.Single(story.Locations);
        var character = Assert.Single(story.Characters);
        Assert.Equal(("KING'S ROAD, CROWNLANDS", LocationSetting.Exterior), (location.Name, location.Setting));
        var elements = story.Containers[0].Scenes[0].Elements;
        Assert.Same(location, elements[0].Location);
        Assert.Same(character, elements[1].Speaker);
    }

    [Fact]
    public void Cue_extensions_are_vocabulary_labels_even_when_left_on_the_speaker()
    {
        var draft = StoryJson.Deserialize<StoryDraft>("""
            {
              "title": "In the Name of the Mother",
              "characters": [{ "name": "ASHFORD SEPTON", "kind": "Human" }, { "name": "EGG", "kind": "Human" }],
              "locations": [{ "name": "ASHFORD MEADOW", "setting": "Exterior" }],
              "containers": [{ "label": "Act I", "scenes": [{ "title": "Meadow", "elements": [
                { "type": "Heading", "location": "ASHFORD MEADOW" },
                { "type": "Dialogue", "speaker": "ASHFORD SEPTON", "extension": "(V.O.)", "text": "May the Seven bear witness." },
                { "type": "Dialogue", "speaker": "EGG (O.C.)", "text": "Up! Up, Thunder!" },
                { "type": "Dialogue", "speaker": "EGG", "extension": "vo", "text": "Hah!" },
                { "type": "Dialogue", "speaker": "EGG (CONT'D)", "text": "Again!" },
                { "type": "Dialogue", "speaker": "EGG", "extension": "filtered", "text": "Can you hear me?" },
                { "type": "Dialogue", "speaker": "ASHFORD SEPTON", "extension": "pre-lap", "text": "Seven." }
              ] }] }],
              "vocabulary": { "extensions": ["(PRE-LAP)", "O.S."] }
            }
            """);

        var story = StoryWriter.Build(draft);

        Assert.Equal(["ASHFORD SEPTON", "EGG"], story.Characters.Select(c => c.Name));
        Assert.Equal(["PRE-LAP", "FILTERED"], story.Vocabulary.Extensions);
        var lines = story.Containers[0].Scenes[0].Elements.Skip(1).ToList();
        Assert.Equal(["V.O.", "O.C.", "V.O.", null, "FILTERED", "PRE-LAP"], lines.Select(e => e.Extension));
        Assert.All(lines, e => Assert.NotNull(e.Speaker));
    }
}
