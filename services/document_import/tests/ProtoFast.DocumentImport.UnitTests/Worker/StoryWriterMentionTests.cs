using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;
using ProtoFast.DocumentImport.Worker.Import;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Worker;

public class StoryWriterMentionTests
{
    [Fact]
    public void Mentions_point_at_the_library_rows_and_invalid_ones_are_dropped()
    {
        var draft = StoryJson.Deserialize<StoryDraft>("""
            {
              "title": "The Quiet Year",
              "characters": [{ "name": "Mara" }],
              "locations": [{ "name": "Harbor Docks", "setting": "Exterior" }],
              "props": [{ "name": "Brass Key" }],
              "containers": [{
                "label": "Act I",
                "scenes": [{
                  "title": "Morning",
                  "elements": [
                    {
                      "type": "Action",
                      "text": "@MARA takes the @brass key to @Harbor Docks.",
                      "mentions": [
                        { "kind": "Character", "name": "Mara", "offset": 0, "length": 5 },
                        { "kind": "Character", "name": "Mara", "offset": 1, "length": 4 },
                        { "kind": "Prop", "name": "Lantern", "offset": 16, "length": 10 },
                        { "kind": "Prop", "name": "Brass Key", "offset": 16, "length": 10 },
                        { "kind": "Location", "name": "Harbor Docks", "offset": 30, "length": 13 },
                        { "kind": "Location", "name": "Harbor Docks", "offset": 40, "length": 13 }
                      ]
                    }
                  ]
                }]
              }]
            }
            """);

        var story = StoryWriter.Build(draft);

        var element = story.Containers[0].Scenes[0].Elements[0];
        Assert.Collection(
            element.Mentions,
            m => Assert.Equal((0, 5, story.Characters[0]), (m.Offset, m.Length, m.Character)),
            m => Assert.Equal((16, 10, story.Props[0]), (m.Offset, m.Length, m.Prop)),
            m => Assert.Equal((30, 13, story.Locations[0]), (m.Offset, m.Length, m.Location)));
    }
}
