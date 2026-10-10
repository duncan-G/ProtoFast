using System.Text;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.DocumentImport.Screenplay.Models;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using LocationSettingRecord = ProtoFast.Data.ThePlot.Entities.LocationSetting;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using ExtraRecord = ProtoFast.Data.ThePlot.Entities.Extra;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using CharacterIdentityRecord = ProtoFast.Data.ThePlot.Entities.CharacterIdentity;
using VoiceProfileRecord = ProtoFast.Data.ThePlot.Entities.VoiceProfile;
using LocationAppearanceRecord = ProtoFast.Data.ThePlot.Entities.LocationAppearance;
using LocationLightingRecord = ProtoFast.Data.ThePlot.Entities.LocationLighting;
using AcousticsRecord = ProtoFast.Data.ThePlot.Entities.Acoustics;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// Has a language model write a library sheet from everything the story says about its subject.
/// What the text states should be kept; what it leaves open is invented once and then canonical.
/// </summary>
public sealed class SheetDescriber(ILanguageModelFactory models, DirectorOptions options, ILogger<SheetDescriber> logger)
{
    private const string System = """
        You write the production bible for an audio-and-picture adaptation of a screenplay: short,
        concrete sheets a voice actor, an illustrator and a sound designer can work from. Keep every
        detail the text states and invent the rest plausibly, consistent with the text and the story's
        world; never contradict the text. Answer with one JSON object in exactly the shape requested
        and nothing else: no prose, no code fences.
        """;

    /// <summary>Writes the sheet onto the character; the caller saves it.</summary>
    public async Task DescribeAsync(
        CharacterRecord character, StoryRecord story, IReadOnlyList<SceneElementRecord> excerpts, CancellationToken ct)
    {
        var kinds = VocabularyLabels.CharacterKinds(story.Vocabulary);
        var prompt = $$"""
            Character: {{character.Name}} ({{character.Kind}}).
            Everything the story says about or through them, in story order (scene, line):
            {{Excerpts(story, excerpts, character.Id)}}
            Write their sheet. "kind": what they are, exactly one of {{string.Join(", ", kinds)}}.
            "description": one or two sentences on what they look like at a glance: their overall
            appearance, bearing and the impression they give. Describe only what a camera would see;
            leave out the plot, their relationships and what they do in the story.
            "identity": "age", "gender" (empty when the story does not say), "build", "face", "hair", "skin" as short phrases, and "marks": scars,
            glasses, tattoos or other things a picture has to keep (empty when none). "voice":
            "description" (age, texture, pace, accent) and "baseline" (how they deliver a line when
            nothing is marked, e.g. "dry, understated"). "wardrobes": the outfits the text implies,
            at least one, each with a short "label" (two or three words) and a "description".
            Reply: {"kind":"{{kinds[0]}}","description":"…","identity":{"age":"…","gender":"…","build":"…","face":"…","hair":"…","skin":"…","marks":[]},"voice":{"description":"…","baseline":"…"},"wardrobes":[{"label":"…","description":"…"}]}
            """;

        using var json = await AskAsync(prompt, ct);
        var root = json.RootElement;
        var description = ModelJson.Str(root, "description");
        if (description is null)
        {
            throw new ModelOutputException("No description in the answer.");
        }

        character.Description = SheetText.Description(Clip(description, SheetText.DescriptionMaxLength));
        if (kinds.FirstOrDefault(k => LibraryNames.Same(k, ModelJson.Str(root, "kind") ?? "")) is { } kind)
        {
            character.Kind = kind;
        }

        if (root.TryGetProperty("identity", out var identity))
        {
            character.Appearance = new CharacterIdentityRecord
            {
                Age = Field(identity, "age"),
                Gender = Field(identity, "gender"),
                Build = Field(identity, "build"),
                Face = Field(identity, "face"),
                Hair = Field(identity, "hair"),
                Skin = Field(identity, "skin"),
                Marks = ModelJson.Strings(identity, "marks").Select(m => Clip(m, SheetText.FieldMaxLength)).ToList(),
            };
        }

        if (root.TryGetProperty("voice", out var voice))
        {
            character.Voice = new VoiceProfileRecord
            {
                Description = Field(voice, "description"),
                Baseline = Field(voice, "baseline"),
                VoiceId = character.Voice?.VoiceId,
                VoiceName = character.Voice?.VoiceName ?? "",
                PreviewUrl = character.Voice?.PreviewUrl ?? "",
            };
        }

        var wardrobes = ModelJson.Array(root, "wardrobes")
            .Select(w => new Wardrobe
            {
                Label = Clip(ModelJson.Str(w, "label") ?? "", LibraryNames.MaxLength),
                Description = Clip(ModelJson.Str(w, "description") ?? "", SheetText.FieldMaxLength),
            })
            .Where(w => w.Label.Length > 0)
            .GroupBy(w => w.Label.ToLowerInvariant())
            .Select(g => g.First())
            .ToList();
        if (wardrobes.Count > 0)
        {
            // Existing wardrobes stay, so looks that wear them keep their ids; new labels are added.
            var merged = character.Wardrobes.OrderBy(w => w.Position).Select(StoryMessages.ToMessage).ToList();
            merged.AddRange(wardrobes.Where(w => merged.All(m => !LibraryNames.Same(m.Label, w.Label))));
            StoryLibrary.ApplyWardrobes(character, merged);
        }
    }

    public async Task DescribeAsync(
        LocationRecord location, StoryRecord story, IReadOnlyList<SceneElementRecord> excerpts, CancellationToken ct)
    {
        var timesOfDay = excerpts
            .Where(e => e.Type == SceneElementTypeRecord.Heading && e.LocationId == location.Id && e.TimeOfDay is not null)
            .Select(e => e.TimeOfDay!)
            .Distinct()
            .ToList();
        if (timesOfDay.Count == 0)
        {
            timesOfDay = ["DAY"];
        }

        var prompt = $$"""
            Location: {{(location.Setting == LocationSettingRecord.Interior ? "INT." : "EXT.")}} {{location.Name}}.
            Everything the story says about it, in story order (scene, line):
            {{Excerpts(story, excerpts, null)}}
            Write its sheet. "description": one or two sentences on what the place looks like: what kind
            of place it is, its size, architecture, materials, condition and atmosphere. Describe only
            what a camera would see; leave out the plot, the characters and what happens there.
            "layout": how the space is arranged, as a plan rather than a view: what is where, and on which
            side as you come in by the main way in, so any camera position in it can be worked out. No
            camera, shot, framing or perspective words. "keyFeatures": the things a picture of it should always show.
            "lighting": one entry per time of day used here ({{string.Join(", ", timesOfDay)}}) with "timeOfDay"
            spelled exactly so and a "description" of the light. "acoustics": "space" (the room sound,
            e.g. "small tiled kitchen") and "ambience" (what is heard under the scene).
            Reply: {"description":"…","layout":"…","keyFeatures":"…","lighting":[{"timeOfDay":"{{timesOfDay[0]}}","description":"…"}],"acoustics":{"space":"…","ambience":"…"} }
            """;

        using var json = await AskAsync(prompt, ct);
        var root = json.RootElement;
        var description = ModelJson.Str(root, "description") ?? throw new ModelOutputException("No description in the answer.");
        location.Description = SheetText.Description(Clip(description, SheetText.DescriptionMaxLength));
        var labels = VocabularyLabels.TimesOfDay(story.Vocabulary);
        location.Appearance = new LocationAppearanceRecord
        {
            Layout = Field(root, "layout"),
            KeyFeatures = Field(root, "keyFeatures"),
            Lighting = ModelJson.Array(root, "lighting")
                .Select(l => (Time: labels.FirstOrDefault(t => LibraryNames.Same(t, ModelJson.Str(l, "timeOfDay") ?? "")), Text: Field(l, "description")))
                .Where(l => l.Time is not null && l.Text.Length > 0)
                .GroupBy(l => l.Time)
                .Select(g => new LocationLightingRecord { TimeOfDay = g.Key!, Description = g.First().Text })
                .ToList(),
        };
        if (root.TryGetProperty("acoustics", out var acoustics))
        {
            location.Acoustics = new AcousticsRecord { Space = Field(acoustics, "space"), Ambience = Field(acoustics, "ambience") };
        }
    }

    public async Task DescribeAsync(
        PropRecord prop, StoryRecord story, IReadOnlyList<SceneElementRecord> excerpts, CancellationToken ct)
    {
        var prompt = $$"""
            Prop: {{prop.Name}}.
            Everything the story says about it, in story order (scene, line):
            {{Excerpts(story, excerpts, null)}}
            Write its sheet: "description", one or two sentences saying what it is and what it looks
            like: its size, shape, materials, colour and condition, concrete enough to draw the same
            object every time. Describe only what a camera would see; leave out the plot, who owns or
            uses it and what happens to it.
            Reply: {"description":"…"}
            """;

        using var json = await AskAsync(prompt, ct);
        var description = ModelJson.Str(json.RootElement, "description") ?? throw new ModelOutputException("No description in the answer.");
        prop.Description = SheetText.Description(Clip(description, SheetText.DescriptionMaxLength));
    }

    /// <param name="excerpts">The scenes they are in.</param>
    public async Task DescribeAsync(
        ExtraRecord extra, StoryRecord story, IReadOnlyList<SceneElementRecord> excerpts, CancellationToken ct)
    {
        var current = string.Join(" ", new[]
        {
            extra.Headcount is { } count ? $"Headcount: {count}." : "",
            string.IsNullOrWhiteSpace(extra.Description) ? "" : extra.Description.Trim(),
            string.IsNullOrWhiteSpace(extra.Wardrobe) ? "" : $"Wearing: {extra.Wardrobe.Trim()}",
        }.Where(p => p.Length > 0));
        var prompt = $$"""
            Extras: {{extra.Name}}. Uncast people who appear in the background of pictures: one person
            or a group. {{(current.Length == 0 ? "" : $"Their sheet so far: {current}")}}
            The scenes they appear in, in story order (scene, line):
            {{Excerpts(story, excerpts, null)}}
            Write their sheet. "headcount": how many people they are, 1 for one person (a guess for a
            crowd); keep the headcount so far unless the scenes contradict it. "description": one or two
            sentences on who they are and how many, their ages and general look, concrete enough to
            draw the same people in every scene; no names, no plot. "wardrobe": what they wear, in a
            short phrase or sentence.
            Reply: {"headcount":1,"description":"…","wardrobe":"…"}
            """;

        using var json = await AskAsync(prompt, ct);
        var description = ModelJson.Str(json.RootElement, "description") ?? throw new ModelOutputException("No description in the answer.");
        extra.Description = SheetText.Description(Clip(description, SheetText.DescriptionMaxLength));
        extra.Wardrobe = SheetText.Description(Clip(ModelJson.Str(json.RootElement, "wardrobe") ?? "", SheetText.DescriptionMaxLength));
        extra.Headcount = Headcount.FromModel(ModelJson.Int(json.RootElement, "headcount")) ?? extra.Headcount;
    }

    /// <summary>The excerpts in story order, earliest first, capped at <see cref="DirectorOptions.MaxExcerpts"/>.</summary>
    private string Excerpts(StoryRecord story, IReadOnlyList<SceneElementRecord> excerpts, Guid? speakerId)
    {
        var sceneOrder = story.Containers.OrderBy(c => c.Position).SelectMany(c => c.Scenes.OrderBy(s => s.Position))
            .Select((s, index) => (s.Id, Number: index + 1, s.Title))
            .ToDictionary(s => s.Id, s => (s.Number, s.Title));
        var text = new StringBuilder();
        var lines = excerpts
            .Where(e => sceneOrder.ContainsKey(e.SceneId))
            .OrderBy(e => sceneOrder[e.SceneId].Number).ThenBy(e => e.Position)
            .Take(options.MaxExcerpts);
        foreach (var element in lines)
        {
            var (number, _) = sceneOrder[element.SceneId];
            var speaker = element.Type == SceneElementTypeRecord.Dialogue
                ? (element.SpeakerId == speakerId ? "they say" : $"{story.Characters.FirstOrDefault(c => c.Id == element.SpeakerId)?.Name ?? "someone"} says")
                : element.Type.ToString().ToLowerInvariant();
            text.Append("- scene ").Append(number).Append(", ").Append(speaker).Append(": ").AppendLine(SceneBrief.Plain(element.Text));
        }

        return text.Length == 0 ? "- (the text says nothing yet; use the name and kind)\n" : text.ToString();
    }

    private async Task<System.Text.Json.JsonDocument> AskAsync(string prompt, CancellationToken ct)
    {
        var model = models.For(options.ModelClass);
        var reply = await model.CompleteAsync(System, prompt, ct);
        try
        {
            return ModelJson.Parse(reply.Text);
        }
        catch (ModelOutputException first)
        {
            logger.LogWarning("{Model} answered outside the requested shape ({Reason}); asking again", model.ModelId, first.Message);
            var again = await model.CompleteAsync(
                System, prompt + "\n\nYour previous answer was not a JSON object in the requested shape. Reply with the JSON object only.", ct);
            return ModelJson.Parse(again.Text);
        }
    }

    private static string Field(System.Text.Json.JsonElement element, string name) =>
        Clip(ModelJson.Str(element, name) ?? "", SheetText.FieldMaxLength);

    private static string Clip(string text, int max)
    {
        var collapsed = Performances.Collapse(text);
        return collapsed.Length <= max ? collapsed : collapsed[..max];
    }
}
