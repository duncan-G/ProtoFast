using System.Text.Json.Nodes;
using Grpc.Core;
using ProtoFast.Api.Services.Screenplays.Files;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class StoryFileTests(StoryDatabase database)
{
    private const string NightShift = """
        {
          "format": "theplot",
          "version": 1,
          "title": "Night Shift",
          "vocabulary": {
            "timesOfDay": [],
            "transitions": [],
            "extensions": [],
            "characterKinds": []
          },
          "characters": [
            {
              "id": "c1",
              "name": "Mara",
              "kind": "Human",
              "hue": 25
            }
          ],
          "locations": [
            {
              "id": "l1",
              "name": "Docks",
              "setting": "exterior",
              "hue": 200
            }
          ],
          "props": [],
          "containers": [
            {
              "label": "Act I",
              "scenes": [
                {
                  "title": "Untitled scene",
                  "elements": [
                    {
                      "type": "heading",
                      "location": "l1",
                      "timeOfDay": "NIGHT"
                    },
                    {
                      "type": "action",
                      "text": "@Mara waits. She sighs.",
                      "mentions": [
                        {
                          "offset": 0,
                          "length": 5,
                          "character": "c1"
                        },
                        {
                          "offset": 13,
                          "length": 3,
                          "character": "c1",
                          "tag": true
                        }
                      ]
                    },
                    {
                      "type": "dialogue",
                      "text": "Come on.",
                      "speaker": "c1",
                      "extension": "V.O.",
                      "parenthetical": "whispering"
                    },
                    {
                      "type": "transition",
                      "transition": "CUT TO"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private readonly Writer _writer = new(database);

    [Fact]
    public async Task A_story_exports_as_theplot_json()
    {
        var story = await _writer.CreateStoryAsync("Night Shift");
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var docks = await _writer.CreateLocationAsync(story.Id, "Docks");
        const string text = "@Mara waits. She sighs.";
        await _writer.WriteFirstSceneAsync(
            story,
            Heading(docks.Id, "NIGHT"),
            Action(text, Mention(text, "@Mara", mara), Tag(text, "She", mara)),
            With(Dialogue(mara.Id, "Come on.", "(whispering)"), r => r.Extension = "V.O."),
            Transition("CUT TO"));

        var file = await _writer.ExportStoryAsync(story.Id);

        Assert.Equal("Night Shift.theplot", file.FileName);
        Assert.Equal(NightShift, file.Content.ToStringUtf8());
    }

    [Fact]
    public async Task An_imported_story_exports_to_the_same_bytes()
    {
        var story = await _writer.CreateStoryAsync("The Signal in the Scrap");
        await SaveVocabularyAsync(story.Id, ["GOLDEN HOUR", "TEATIME"], "Ghost");
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara", "Ghost");
        var ben = await _writer.CreateCharacterAsync(story.Id, "Ben");
        var docks = await _writer.CreateLocationAsync(story.Id, "Docks");
        var radio = await _writer.CreatePropAsync(story.Id, "Radio");
        const string text = "@Ben hands @Mara the @Radio at the @Docks. “Here.”";
        await _writer.WriteFirstSceneAsync(
            story,
            Heading(docks.Id, "TEATIME"),
            Action(text, Mention(text, "@Ben", ben), Mention(text, "@Mara", mara), Mention(text, "@Radio", radio),
                Mention(text, "@Docks", docks)),
            Dialogue(null, "Who’s there?"),
            Beat(SceneElementType.Narration, "Nobody answers."));
        var act = (await _writer.Call((s, c) => s.CreateContainer(
            new CreateContainerRequest { StoryId = story.Id, Label = "Act II" }, c))).Container;
        await _writer.CreateSceneAsync(act.Id, 0, "Dawn", Heading(docks.Id, "GOLDEN HOUR"), Transition("FADE OUT"));
        // The heading keeps its label after the vocabulary drops it, as the editor allows.
        await SaveVocabularyAsync(story.Id, ["GOLDEN HOUR"], "Ghost");

        var exported = await _writer.ExportStoryAsync(story.Id);
        var imported = await _writer.ImportStoryAsync(exported.Content.ToByteArray());
        var again = await _writer.ExportStoryAsync(imported.Id);

        Assert.NotEqual(story.Id, imported.Id);
        Assert.Equal("The Signal in the Scrap", imported.Title);
        Assert.Equal(exported.Content.ToStringUtf8(), again.Content.ToStringUtf8());
        var copy = await _writer.GetStoryAsync(imported.Id);
        Assert.Equal(["Act I", "Act II"], copy.Containers.Select(c => c.Label));
        Assert.DoesNotContain(copy.Characters, c => c.Id == mara.Id || c.Id == ben.Id);
        var heading = (await _writer.GetSceneAsync(copy.Containers[0].Scenes[0].Id)).Elements[0];
        Assert.Equal(copy.Locations.Single().Id, heading.LocationId);
        Assert.Equal("TEATIME", heading.TimeOfDay);
    }

    [Fact]
    public async Task The_same_file_imports_as_a_new_story_each_time()
    {
        var first = await _writer.ImportStoryAsync(NightShift);
        var second = await _writer.ImportStoryAsync(NightShift);

        Assert.NotEqual(first.Id, second.Id);
        var stories = (await _writer.Call((s, c) => s.ListStories(new ListStoriesRequest(), c))).Stories;
        Assert.Equal(2, stories.Count(s => s.Title == "Night Shift"));
    }

    [Fact]
    public async Task Another_writer_cannot_export_the_story()
    {
        var story = await _writer.CreateStoryAsync();
        var stranger = new Writer(database);

        var error = await Assert.ThrowsAsync<RpcException>(() => stranger.ExportStoryAsync(story.Id));

        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Theory]
    [InlineData("not json", "This isn’t a .theplot file.")]
    [InlineData("another format", "This isn’t a .theplot file.")]
    [InlineData("newer version", "This file was saved by a newer version of ThePlot, so it can’t be imported yet.")]
    [InlineData("unknown member", "This .theplot file has something ThePlot can’t read at $.characters[0].colour.")]
    [InlineData("unknown type", "This .theplot file has something ThePlot can’t read at $.containers[0].scenes[0].elements[0].type.")]
    [InlineData("numeric setting", "This .theplot file has something ThePlot can’t read at $.locations[0].setting.")]
    [InlineData("null title", "This .theplot file has something ThePlot can’t read at $.title.")]
    [InlineData("missing hue", "This .theplot file has something ThePlot can’t read at $.characters[0].")]
    [InlineData("shared id", "Two characters in the file share the id “c1”.")]
    [InlineData("same name", "Character “mara” — There’s already a character called “Mara”.")]
    [InlineData("bad hue", "Character “Mara” — Hues run from 0 to 359.")]
    [InlineData("unknown speaker", "“Act I”, scene 1 — Row 3: the file has no character with the id “c9”.")]
    [InlineData("mention outside the text", "“Act I”, scene 1 — Row 2: a mention runs outside the text.")]
    [InlineData("mention with two targets", "“Act I”, scene 1 — Row 2: a mention points at one character, location or prop, not several.")]
    [InlineData("dialogue with a location", "“Act I”, scene 1 — Row 3: dialogue lines can’t have a location.")]
    [InlineData("no scenes", "A story needs at least one scene.")]
    public async Task Files_that_break_the_editors_rules_are_refused(string problem, string message)
    {
        var file = JsonNode.Parse(NightShift)!.AsObject();
        var character = file["characters"]![0]!.AsObject();
        var elements = file["containers"]![0]!["scenes"]![0]!["elements"]!.AsArray();
        switch (problem)
        {
            case "another format": file["format"] = "fountain"; break;
            case "newer version": file["version"] = TheplotFile.CurrentVersion + 1; break;
            case "unknown member": character["colour"] = "red"; break;
            case "unknown type": elements[0]!["type"] = "montage"; break;
            case "numeric setting": file["locations"]![0]!["setting"] = 1; break;
            case "null title": file["title"] = null; break;
            case "missing hue": character.Remove("hue"); break;
            case "shared id": file["characters"]!.AsArray().Add(new JsonObject { ["id"] = "c1", ["name"] = "Ben", ["kind"] = "Human", ["hue"] = 0 }); break;
            case "same name": file["characters"]!.AsArray().Add(new JsonObject { ["id"] = "c2", ["name"] = "mara", ["kind"] = "Human", ["hue"] = 0 }); break;
            case "bad hue": character["hue"] = 400; break;
            case "unknown speaker": elements[2]!["speaker"] = "c9"; break;
            case "mention outside the text": elements[1]!["mentions"]![0]!["length"] = 99; break;
            case "mention with two targets": elements[1]!["mentions"]![0]!["location"] = "l1"; break;
            case "dialogue with a location": elements[2]!["location"] = "l1"; break;
            case "no scenes": file["containers"]![0]!["scenes"] = new JsonArray(); break;
        }

        var json = problem == "not json" ? "{ \"format\": " : file.ToJsonString();
        var error = await Assert.ThrowsAsync<RpcException>(() => _writer.ImportStoryAsync(json));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal(message, error.Status.Detail);
    }

    [Theory]
    [InlineData("Night Shift", "Night Shift.theplot")]
    [InlineData("  The Signal / in: the Scrap. ", "The Signal in the Scrap.theplot")]
    [InlineData("...", "Story.theplot")]
    public void File_names_drop_what_file_systems_reserve(string title, string fileName) =>
        Assert.Equal(fileName, TheplotJson.FileName(title));

    private Task<SaveVocabularyReply> SaveVocabularyAsync(string storyId, string[] timesOfDay, string kind)
    {
        var vocabulary = new StoryVocabulary { TimesOfDay = { timesOfDay } };
        vocabulary.Transitions.Add("WIPE TO");
        vocabulary.CharacterKinds.Add(new CharacterKind { Label = kind, AvatarShape = AvatarShape.Star });
        return _writer.Call((s, c) => s.SaveVocabulary(
            new SaveVocabularyRequest { StoryId = storyId, Vocabulary = vocabulary }, c));
    }
}
