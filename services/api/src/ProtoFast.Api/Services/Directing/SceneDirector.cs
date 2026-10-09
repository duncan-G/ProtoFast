using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.DocumentImport.Screenplay.Models;
using CharacterWardrobeRecord = ProtoFast.Data.ThePlot.Entities.CharacterWardrobe;
using CueRecord = ProtoFast.Data.ThePlot.Entities.Cue;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using DirectionVocabulary = ProtoFast.Data.ThePlot.Entities.DirectionVocabulary;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;
using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
using SceneLookChangeRecord = ProtoFast.Data.ThePlot.Entities.SceneLookChange;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using ScenePropStateRecord = ProtoFast.Data.ThePlot.Entities.ScenePropState;
using SceneLocationStateRecord = ProtoFast.Data.ThePlot.Entities.SceneLocationState;

using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using FramePlacementRecord = ProtoFast.Data.ThePlot.Entities.FramePlacement;
using FramePropRecord = ProtoFast.Data.ThePlot.Entities.FrameProp;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// Has a language model direct a scene, one part at a time. Code cuts and the model labels: the
/// model picks tags, frame boundaries, shots, placements and looks by label and name, and code
/// assembles every stored value from the scene's own text and ids. Rows the author edited are kept
/// unless the caller says otherwise.
/// </summary>
public sealed class SceneDirector(ILanguageModelFactory models, DirectorOptions options, ILogger<SceneDirector> logger)
{
    private const string System = """
        You are the director of an audio-and-picture adaptation of a screenplay. You read a scene and
        decide only what you are asked for: how lines are delivered, which pictures show the scene,
        what everyone looks like. You never rewrite the writer's words and you never invent lines.
        Use only the labels (e1, e2, …) and names given to you, spelled exactly as given. Answer with
        one JSON object in exactly the shape requested and nothing else: no prose, no code fences.
        """;

    /// <summary>The picture is drawn from a frame's "visible", so it has to stage the whole frame.</summary>
    private const string VisibleRule = """
        "visible" is what the picture shows, in two to four present-tense sentences, written so an
        illustrator could draw it without the script. Name everyone on screen and say what each is
        doing. Where two or more are on screen, say how they are placed and turned toward each other
        (facing across the table, back to back, one behind the other, an arm's length apart). Place
        each of them in the location (at the window, on the bottom stair, behind the bar). Then
        anything else happening in the picture: props in use, people or animals in the background,
        weather, light, smoke, movement. Only what a camera sees: no thoughts, sounds or dialogue.
        Call props by their names as listed.
        """;

    /// <summary>A held prop is part of the look, so frames have to keep it in the holder's hands.</summary>
    private const string HoldingRule = """
        Looks says what each character carries and from which line that changes. In every frame from
        that line until the next change they carry it, in their hands or on them, the same object
        each time: say so in "visible" and in their pose, and never show them without it or with
        something else in its place. On the line where they hand it over, the frame shows the handover.
        """;

    public async Task<DirectionDraft> DirectAsync(DirectedScene context, DirectRequest request, CancellationToken ct)
    {
        var brief = new SceneBrief(context);
        var draft = DirectionDraft.From(context.Scene);
        var replace = request.ReplaceEdited;
        switch (request.TargetCase)
        {
            case DirectRequest.TargetOneofCase.Scene:
                await PresenceAsync(brief, draft, ct);
                await StoryDayAsync(brief, draft, ct);
                await ContinuityAsync(brief, draft, null, null, replace, ct);
                await CuesAsync(brief, draft, null, replace, ct);
                await FramesAsync(brief, draft, replace, false, ct);
                break;
            case DirectRequest.TargetOneofCase.StoryDay:
                await StoryDayAsync(brief, draft, ct);
                break;
            case DirectRequest.TargetOneofCase.Continuity:
                await ContinuityAsync(brief, draft, null, null, replace, ct);
                break;
            case DirectRequest.TargetOneofCase.AppearanceCharacterId:
                await ContinuityAsync(brief, draft, Id(request.AppearanceCharacterId, "That character is no longer in the library."), null, true, ct);
                break;
            case DirectRequest.TargetOneofCase.PropStatePropId:
                await ContinuityAsync(brief, draft, null, Id(request.PropStatePropId, "That prop is no longer in the library."), true, ct);
                break;
            case DirectRequest.TargetOneofCase.Cues:
                await CuesAsync(brief, draft, null, replace, ct);
                break;
            case DirectRequest.TargetOneofCase.CueElementId:
                await CuesAsync(brief, draft, Id(request.CueElementId, DirectionErrors.LineNotFound), true, ct);
                break;
            case DirectRequest.TargetOneofCase.Frames:
                await FramesAsync(brief, draft, replace, true, ct);
                break;
            case DirectRequest.TargetOneofCase.FrameId:
                await FrameAsync(brief, draft, Id(request.FrameId, DirectionErrors.FrameNotFound), ct);
                break;
            default:
                throw StoryErrors.Invalid("Say what to direct.");
        }

        return draft;
    }

    // ─── who and what is in the scene ──────────────────────────────────────

    /// <summary>
    /// Mentions and speakers name only part of a scene: "he", "the guard" and "the gun" point into
    /// the library too. The model picks the library entries the text puts in the scene, and they
    /// join the brief so the later passes give them looks, states and frames. A location is asked
    /// for only where no heading names one.
    /// </summary>
    private async Task PresenceAsync(SceneBrief brief, DirectionDraft draft, CancellationToken ct)
    {
        var characters = brief.Context.Story.Characters.Where(c => !brief.Cast.Contains(c)).ToList();
        var props = brief.Context.Story.Props.Where(p => !brief.Props.Contains(p)).ToList();
        var locations = brief.Unplaced && brief.Locations.Count == 0 ? brief.Context.Story.Locations.ToList() : [];
        if (brief.Elements.Count == 0 || characters.Count + props.Count + locations.Count == 0)
        {
            return;
        }

        static string Entries(IEnumerable<(string Name, string? Description)> entries)
        {
            var text = new StringBuilder();
            foreach (var (name, description) in entries)
            {
                text.Append("- ").Append(name);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    text.Append(": ").Append(description);
                }

                text.AppendLine();
            }

            return text.Length == 0 ? "(none)\n" : text.ToString();
        }

        var prompt = $$"""
            The scene below names some of its characters and props. Find the others: which of the
            library entries listed is in this scene even though the text does not name it, through a
            pronoun, a role ("the guard"), a description or plain context. A character counts only when
            physically present, not when talked about or remembered. A prop counts when it is seen or
            handled. {{(locations.Count == 0 ? "" : "The scene's heading names no place, so also pick the one location from the library where it happens, if the text makes it clear.")}}
            Leave out anything you are unsure of; an empty list is a fine answer. Use names exactly as listed.

            Characters already in the scene:
            {{brief.CastSheet()}}
            Props already in the scene:
            {{brief.PropSheet()}}
            Other characters in the library:
            {{Entries(characters.Select(c => (c.Name, c.Description)))}}
            Other props in the library:
            {{Entries(props.Select(p => (p.Name, p.Description)))}}
            {{(locations.Count == 0 ? "" : $"Locations in the library:\n{Entries(locations.Select(l => (l.Name, l.Description)))}")}}
            Scene {{brief.Heading()}}:
            {{brief.Script()}}
            Reply: {"characters":["Name"],"props":["Name"],"locations":["Name"]}
            """;

        using var json = await AskAsync(prompt, ct);
        var place = ModelJson.Strings(json.RootElement, "locations").Select(brief.LocationNamed)
            .FirstOrDefault(l => l is not null && locations.Contains(l));
        brief.Include(
            ModelJson.Strings(json.RootElement, "characters").Select(brief.CharacterNamed)
                .Where(c => c is not null && characters.Contains(c)).Select(c => c!.Id),
            ModelJson.Strings(json.RootElement, "props").Select(brief.PropNamed)
                .Where(p => p is not null && props.Contains(p)).Select(p => p!.Id),
            place is null ? [] : [place.Id]);
        if (place is not null && draft.LocationStates.All(l => l.LocationId != place.Id))
        {
            draft.LocationStates.Add(new SceneLocationStateRecord { LocationId = place.Id, Change = "" });
        }
    }

    // ─── story day ─────────────────────────────────────────────────────────

    private async Task StoryDayAsync(SceneBrief brief, DirectionDraft draft, CancellationToken ct)
    {
        var earlier = new StringBuilder();
        var number = 1;
        foreach (var scene in brief.Context.Earlier)
        {
            var heading = scene.Elements.OrderBy(e => e.Position).FirstOrDefault(e => e.Type == SceneElementTypeRecord.Heading);
            earlier.Append(number++).Append(". \"").Append(scene.Title).Append("\" ")
                .Append(heading is null ? "no heading" : brief.HeadingText(heading))
                .Append(scene.StoryDay is { } day ? $" (day {day})" : " (day unknown)")
                .AppendLine();
        }

        var prompt = $$"""
            Which day of the story does this scene happen on? Days count from 1. A scene stays on the
            previous scene's day unless its heading or text shows time has passed (a new morning, LATER
            after a night, a date mentioned). CONTINUOUS stays on the same day.

            Earlier scenes, in order:
            {{(earlier.Length == 0 ? "(this is the first scene)\n" : earlier.ToString())}}
            This scene, {{number}}. "{{brief.Context.Scene.Title}}" {{brief.Heading()}}:
            {{brief.Script()}}
            Reply: {"storyDay": 1, "reason": "one sentence"}
            """;

        using var json = await AskAsync(prompt, ct);
        draft.StoryDay = ModelJson.Int(json.RootElement, "storyDay") is { } storyDay and >= 1
            ? storyDay
            : throw new ModelOutputException("No story day in the answer.");
    }

    // ─── continuity: looks and prop states ─────────────────────────────────

    /// <summary>
    /// A look carries forward: on the same story day a character wears what they wore at the end of
    /// their last scene unless a line shows a change, and state (a bandage, a stain) and what they
    /// carry persist until the text removes them. The model gives the look the scene opens with and
    /// the lines that change it; code builds each change into a whole look, so every frame and the
    /// next scene read one look, and a prop someone takes leaves whoever held it.
    /// </summary>
    private async Task ContinuityAsync(
        SceneBrief brief, DirectionDraft draft, Guid? onlyCharacter, Guid? onlyProp, bool replaceEdited, CancellationToken ct)
    {
        var cast = brief.Cast.Where(c => onlyCharacter is null || c.Id == onlyCharacter).ToList();
        if (onlyProp is not null)
        {
            cast = [];
        }

        var previousLooks = cast.ToDictionary(c => c.Id, c => PreviousLook(brief.Context, c.Id));
        var storyProps = brief.Context.Story.Props.Select(p => p.Id).ToHashSet();
        brief.Include([], previousLooks.Values.OfType<(SceneRecord, Look)>().SelectMany(p => p.Item2.Holding).Where(storyProps.Contains), []);

        var props = brief.Props.Where(p => onlyProp is null || p.Id == onlyProp).ToList();
        if (onlyCharacter is not null)
        {
            props = [];
        }

        if (cast.Count == 0 && props.Count == 0 && (onlyCharacter is not null || onlyProp is not null || brief.Locations.Count == 0))
        {
            return;
        }

        var previousStates = props.ToDictionary(p => p.Id, p => PreviousState(brief.Context, p.Id));
        var day = draft.StoryDay?.ToString() ?? "unknown";

        var looks = new StringBuilder();
        foreach (var character in cast)
        {
            looks.Append("- ").Append(character.Name);
            if (!string.IsNullOrWhiteSpace(character.Description))
            {
                looks.Append(" (").Append(character.Description).Append(')');
            }

            looks.AppendLine().Append("  wardrobes on sheet: ")
                .AppendLine(character.Wardrobes.Count == 0
                    ? "(none yet)"
                    : string.Join("; ", character.Wardrobes.Select(w => $"\"{w.Label}\"{(w.Description.Length > 0 ? $" = {w.Description}" : "")}")));
            var previous = previousLooks[character.Id];
            if (previous is null)
            {
                looks.AppendLine("  previous look: (first appearance in the story)");
            }
            else
            {
                var (scene, look) = previous.Value;
                var when = scene.StoryDay is null || draft.StoryDay is null
                    ? "a day not set; treat it as the same day"
                    : scene.StoryDay == draft.StoryDay ? "the same day" : "an earlier day";
                looks.Append("  previous look, as scene \"").Append(scene.Title).Append("\" ended (day ")
                    .Append(scene.StoryDay?.ToString() ?? "unknown").Append(", ").Append(when)
                    .Append("): wardrobe \"")
                    .Append(character.Wardrobes.FirstOrDefault(w => w.Id == look.WardrobeId)?.Label ?? "none")
                    .Append("\", wardrobe change ").Append(Json(look.WardrobeChange))
                    .Append(", appearance change ").Append(Json(look.AppearanceChange))
                    .Append(", holding ").AppendLine(Json(PropNames(brief, look.Holding)));
            }
        }

        var states = new StringBuilder();
        foreach (var prop in props)
        {
            var previous = previousStates[prop.Id];
            states.Append("- ").Append(prop.Name);
            if (!string.IsNullOrWhiteSpace(prop.Description))
            {
                states.Append(" (").Append(prop.Description).Append(')');
            }

            states.Append(": previous state: ")
                .AppendLine(previous is null
                    ? "(first appearance in the story)"
                    : $"scene \"{previous.Value.Scene.Title}\": {Json(previous.Value.State.State)}");
        }

        var locations = new StringBuilder();
        foreach (var location in brief.Locations.Where(_ => onlyCharacter is null && onlyProp is null))
        {
            locations.Append("- ").Append(location.Name);
            if (!string.IsNullOrWhiteSpace(location.Description))
            {
                locations.Append(" (").Append(location.Description).Append(')');
            }

            locations.AppendLine();
        }

        var prompt = $$"""
            Decide how everyone and everything looks in this scene. This scene is on story day {{day}}.

            Continuity comes first. The default for every character is their previous look, carried
            forward unchanged: the same wardrobe, the same state and the same things in their hands.
            Change it only when a line shows it (they changed clothes, got wet, were hurt, washed up,
            picked something up or let it go), and then name that line.
            On the same story day the wardrobe stays unless a line says otherwise. On a later day the
            wardrobe may differ, but lasting state (a bandage, a scar, a torn sleeve) still carries over
            unless the text says it is gone. A first appearance takes the wardrobe on the sheet that fits
            the scene best.

            Each character's look is given twice: as the scene opens, then "changes" for the lines
            during the scene after which it is different, for the rest of the scene and beyond.
            As the scene opens: "present" is false when they are only talked about or remembered and
            never physically in the scene; "wardrobe" is one of the labels on their sheet, spelled
            exactly, or null when the sheet has none that fits; "wardrobeChange" is a short phrase for
            what is different about that outfit ("hood up, soaked through"), empty when nothing;
            "appearanceChange" is a short phrase for what shows on them beyond their sheet (a bandage,
            wet hair), empty when nothing; "holding" names the props, from the props in the scene, they
            carry: in a hand, under an arm, slung over a shoulder, on their back. "changeReason" says why
            the opening look differs from the previous one, or null when it does not differ;
            "changeLine" is the label (e3, …) of the line that shows it, or null.
            Only when the text of this scene describes clothing the sheet has not got, add "newWardrobe"
            with a short "label" and a "description" taken from the text; otherwise leave it null.
            Each change gives its "line" label and only what changes from that line on: "takes" names
            props they pick up or are handed, "releases" props they put down, hand over, drop, throw or
            have taken from them; "wardrobe", "wardrobeChange" and "appearanceChange" give the new
            value, or null when it stays; "reason" says what happens in a few words. A handover is a
            change for both people on the same line: one releases, the other takes. What happens during
            the scene goes in "changes", never in the opening look. A gesture or a passing moment
            (raising a hand, flinching) is not a change; leave "changes" empty when nothing changes.
            A prop someone carries is the same object in every scene until they let it go: never a
            different one, never gone without a line that says so.
            For each prop: "state" ("broken", "lit"; empty means as described on its sheet) carries
            forward from its previous state across any number of days until a line changes it, with the
            same "changeReason" and "changeLine".
            For each location: "change" is a short phrase for what this scene shows that its sheet does
            not (weather, damage, how the place is lit), taken from the text; empty when nothing. The
            heading already gives the time of day, so never repeat it.

            Scene {{brief.Heading()}}:
            {{brief.Script()}}
            Characters:
            {{(looks.Length == 0 ? "(none)\n" : looks.ToString())}}
            Props in the scene:
            {{brief.PropSheet()}}
            Prop states to decide:
            {{(states.Length == 0 ? "(none)\n" : states.ToString())}}
            Locations:
            {{(locations.Length == 0 ? "(none)\n" : locations.ToString())}}
            Reply: {"looks":[{"name":"Name","present":true,"wardrobe":"label or null","newWardrobe":null,"wardrobeChange":"","appearanceChange":"","holding":["Prop"],"changeReason":null,"changeLine":null,"changes":[{"line":"e5","takes":[],"releases":["Prop"],"wardrobe":null,"wardrobeChange":null,"appearanceChange":null,"reason":"hands it over"}]}],"props":[{"name":"Name","state":[],"changeReason":null,"changeLine":null}],"locations":[{"name":"Name","change":""}]}
            """;

        using var json = await AskAsync(prompt, ct);
        var answeredLooks = ModelJson.Array(json.RootElement, "looks")
            .Select(l => (Character: brief.CharacterNamed(ModelJson.Str(l, "name") ?? ModelJson.Str(l, "character")), Json: l))
            .Where(l => l.Character is not null)
            .GroupBy(l => l.Character!.Id)
            .ToDictionary(g => g.Key, g => g.First().Json);
        var answeredStates = ModelJson.Array(json.RootElement, "props")
            .Select(p => (Prop: brief.PropNamed(ModelJson.Str(p, "name") ?? ModelJson.Str(p, "prop")), Json: p))
            .Where(p => p.Prop is not null)
            .GroupBy(p => p.Prop!.Id)
            .ToDictionary(g => g.Key, g => g.First().Json);
        if (answeredLooks.Count == 0 && answeredStates.Count == 0 && cast.Count + props.Count > 0)
        {
            throw new ModelOutputException("No looks or prop states in the answer.");
        }

        var directed = new List<Guid>();
        foreach (var character in cast)
        {
            var existing = draft.Appearances.FirstOrDefault(a => a.CharacterId == character.Id);
            if (existing is { Edited: true } && !replaceEdited)
            {
                continue;
            }

            // The previous look is the starting point; the answer only moves away from it on evidence.
            var previous = previousLooks[character.Id]?.Look;
            var look = new SceneAppearanceRecord
            {
                Id = existing?.Id ?? Guid.Empty,
                CharacterId = character.Id,
                OnScreen = existing?.OnScreen ?? true,
                WardrobeId = previous?.WardrobeId is { } carried && character.Wardrobes.Any(w => w.Id == carried) ? carried : null,
                WardrobeChange = previous?.WardrobeChange ?? "",
                AppearanceChange = previous?.AppearanceChange ?? "",
                Holding = [.. previous?.Holding.Where(storyProps.Contains) ?? []],
            };
            answeredLooks.TryGetValue(character.Id, out var answer);
            if (answer.ValueKind == JsonValueKind.Object)
            {
                look.ChangeReason = Reason(ModelJson.Str(answer, "changeReason"));
                look.ChangeElementId = look.ChangeReason is null ? null : brief.Element(ModelJson.Str(answer, "changeLine"))?.Id;
                var changed = look.ChangeReason is not null || previous is null;
                var label = ModelJson.Str(answer, "wardrobe");
                var wardrobe = label is null ? null : character.Wardrobes.FirstOrDefault(w => LibraryNames.Same(w.Label, label));
                if (answer.TryGetProperty("newWardrobe", out var added) && added.ValueKind == JsonValueKind.Object
                    && ModelJson.Str(added, "label") is { Length: > 0 } newLabel && changed)
                {
                    var clean = Trim(newLabel, LibraryNames.MaxLength);
                    look.Wardrobe = character.Wardrobes.FirstOrDefault(w => LibraryNames.Same(w.Label, clean))
                                    ?? new CharacterWardrobeRecord
                                    {
                                        CharacterId = character.Id,
                                        Label = clean,
                                        Description = Trim(ModelJson.Str(added, "description") ?? "", SheetText.FieldMaxLength),
                                    };
                    look.WardrobeId = look.Wardrobe.Id == Guid.Empty ? null : look.Wardrobe.Id;
                }
                else if (wardrobe is not null && (changed || look.WardrobeId is null))
                {
                    look.WardrobeId = wardrobe.Id;
                }

                look.OnScreen = !answer.TryGetProperty("present", out var present) || present.ValueKind != JsonValueKind.False;
                // A change the answer leaves out carries forward; one it gives replaces it on evidence.
                if (ModelJson.Str(answer, "wardrobeChange") is { } wardrobeChange
                    && (changed || wardrobeChange.Trim().Length > 0))
                {
                    look.WardrobeChange = Trim(wardrobeChange, DirectionValidator.BeatMaxLength);
                }

                if (ModelJson.Str(answer, "appearanceChange") is { } appearanceChange
                    && (changed || appearanceChange.Trim().Length > 0))
                {
                    look.AppearanceChange = Trim(appearanceChange, DirectionValidator.BeatMaxLength);
                }

                // Something carried in is only put down on evidence; something picked up is kept.
                if (answer.TryGetProperty("holding", out var holding) && holding.ValueKind == JsonValueKind.Array)
                {
                    var held = Held(brief, ModelJson.Strings(answer, "holding"));
                    look.Holding = changed ? held : [.. look.Holding.Union(held)];
                }
            }

            draft.Appearances.RemoveAll(a => a.CharacterId == character.Id);
            draft.Appearances.Add(look);

            var kept = draft.LookChanges.Where(c => c.CharacterId == character.Id && c.Edited && !replaceEdited).ToList();
            draft.LookChanges.RemoveAll(c => c.CharacterId == character.Id);
            draft.LookChanges.AddRange(kept);
            if (answer.ValueKind == JsonValueKind.Object)
            {
                draft.LookChanges.AddRange(Changes(brief, character, look, answer, kept));
            }

            directed.Add(character.Id);
        }

        if (onlyCharacter is null)
        {
            OneHolderAtATime(brief, draft, directed);
        }

        foreach (var prop in props)
        {
            var existing = draft.PropStates.FirstOrDefault(p => p.PropId == prop.Id);
            if (existing is { Edited: true } && !replaceEdited)
            {
                continue;
            }

            var previous = previousStates[prop.Id]?.State;
            var state = new ScenePropStateRecord
            {
                Id = existing?.Id ?? Guid.Empty,
                PropId = prop.Id,
                InView = existing?.InView ?? true,
                State = [.. previous?.State ?? []],
            };
            if (answeredStates.TryGetValue(prop.Id, out var answer))
            {
                state.ChangeReason = Reason(ModelJson.Str(answer, "changeReason"));
                state.ChangeElementId = state.ChangeReason is null ? null : brief.Element(ModelJson.Str(answer, "changeLine"))?.Id;
                var answered = Short(ModelJson.Strings(answer, "state"));
                if (state.ChangeReason is not null || previous is null || answered.Count > 0)
                {
                    state.State = answered;
                }
            }

            draft.PropStates.RemoveAll(p => p.PropId == prop.Id);
            draft.PropStates.Add(state);
        }

        if (onlyCharacter is not null || onlyProp is not null)
        {
            return;
        }

        var answeredLocations = ModelJson.Array(json.RootElement, "locations")
            .Select(l => (Location: brief.LocationNamed(ModelJson.Str(l, "name") ?? ModelJson.Str(l, "location")), Json: l))
            .Where(l => l.Location is not null)
            .GroupBy(l => l.Location!.Id)
            .ToDictionary(g => g.Key, g => g.First().Json);
        foreach (var location in brief.Locations)
        {
            var existing = draft.LocationStates.FirstOrDefault(l => l.LocationId == location.Id);
            if ((existing is { Edited: true } && !replaceEdited) || !answeredLocations.TryGetValue(location.Id, out var answer))
            {
                continue;
            }

            draft.LocationStates.RemoveAll(l => l.LocationId == location.Id);
            draft.LocationStates.Add(new SceneLocationStateRecord
            {
                Id = existing?.Id ?? Guid.Empty,
                LocationId = location.Id,
                Change = Trim(ModelJson.Str(answer, "change") ?? "", DirectionValidator.BeatMaxLength),
            });
        }
    }

    /// <summary>
    /// The answer's changes for one character as whole looks, in line order: each starts from the
    /// look before it and applies only what the answer says changes. Lines the author's own changes
    /// sit on are left to them; a change that changes nothing is dropped.
    /// </summary>
    private static List<SceneLookChangeRecord> Changes(
        SceneBrief brief, CharacterRecord character, SceneAppearanceRecord opening, JsonElement answer, List<SceneLookChangeRecord> kept)
    {
        List<SceneLookChangeRecord> changes = [];
        var running = Look.Of(opening);
        var addedWardrobe = opening.Wardrobe;
        var steps = ModelJson.Array(answer, "changes")
            .Select(c => (Index: brief.Index(ModelJson.Str(c, "line") ?? ModelJson.Str(c, "changeLine")), Json: c))
            .Where(c => c.Index is not null)
            .OrderBy(c => c.Index);
        foreach (var (index, step) in steps)
        {
            var element = brief.Elements[index!.Value];
            if (kept.Any(c => c.ElementId == element.Id))
            {
                continue;
            }

            var wardrobeId = running.WardrobeId;
            if (ModelJson.Str(step, "wardrobe") is { } label
                && character.Wardrobes.FirstOrDefault(w => LibraryNames.Same(w.Label, label)) is { } wardrobe)
            {
                wardrobeId = wardrobe.Id;
                addedWardrobe = null;
            }

            var releases = Held(brief, ModelJson.Strings(step, "releases"));
            var holding = running.Holding.Where(id => !releases.Contains(id))
                .Concat(Held(brief, ModelJson.Strings(step, "takes")))
                .Distinct()
                .ToList();
            var next = new Look(
                wardrobeId,
                ModelJson.Str(step, "wardrobeChange") is { } wardrobeChange ? Trim(wardrobeChange, DirectionValidator.BeatMaxLength) : running.WardrobeChange,
                ModelJson.Str(step, "appearanceChange") is { } appearanceChange ? Trim(appearanceChange, DirectionValidator.BeatMaxLength) : running.AppearanceChange,
                holding);
            if (next.SameAs(running))
            {
                continue;
            }

            changes.RemoveAll(c => c.ElementId == element.Id);
            changes.Add(new SceneLookChangeRecord
            {
                CharacterId = character.Id,
                ElementId = element.Id,
                WardrobeId = next.WardrobeId,
                Wardrobe = addedWardrobe,
                WardrobeChange = next.WardrobeChange,
                AppearanceChange = next.AppearanceChange,
                Holding = [.. next.Holding],
                Reason = Reason(ModelJson.Str(step, "reason")),
            });
            running = next;
        }

        return changes;
    }

    /// <summary>
    /// A prop taken on a line leaves whoever held it there: their look loses it from that line on,
    /// until a later change of theirs takes it back.
    /// </summary>
    private static void OneHolderAtATime(SceneBrief brief, DirectionDraft draft, IReadOnlyCollection<Guid> directed)
    {
        var takes = draft.LookChanges
            .Where(c => directed.Contains(c.CharacterId))
            .Select(c => (Change: c, Before: Looks.At(draft.Appearances, draft.LookChanges, brief.Position, c.CharacterId, brief.Position(c.ElementId) - 1).Look))
            .SelectMany(t => t.Change.Holding.Where(id => !t.Before.Holding.Contains(id)).Select(prop => (t.Change, Prop: prop)))
            .OrderBy(t => brief.Position(t.Change.ElementId))
            .ToList();
        foreach (var (take, prop) in takes)
        {
            var at = brief.Position(take.ElementId);
            foreach (var other in brief.Cast.Where(c => c.Id != take.CharacterId && directed.Contains(c.Id)))
            {
                var current = Looks.At(draft.Appearances, draft.LookChanges, brief.Position, other.Id, at);
                if (!current.Look.Holding.Contains(prop))
                {
                    continue;
                }

                // Later changes carried it on from before the take, unless one picks it up again.
                var later = Looks.Changes(draft.LookChanges, brief.Position, other.Id).Where(c => brief.Position(c.ElementId) > at).ToList();
                var heldBefore = later.Select(c => c.Holding.Contains(prop)).ToList();
                var name = brief.Context.Story.Props.FirstOrDefault(p => p.Id == prop)?.Name ?? "it";
                if (current.Change is { } same && same.ElementId == take.ElementId)
                {
                    same.Holding.Remove(prop);
                }
                else
                {
                    draft.LookChanges.Add(new SceneLookChangeRecord
                    {
                        CharacterId = other.Id,
                        ElementId = take.ElementId,
                        WardrobeId = current.Look.WardrobeId,
                        Wardrobe = current.Change is null ? current.Opening?.Wardrobe : current.Change.Wardrobe,
                        WardrobeChange = current.Look.WardrobeChange,
                        AppearanceChange = current.Look.AppearanceChange,
                        Holding = current.Look.Holding.Where(id => id != prop).ToList(),
                        Reason = Trim($"lets go of the {name}", DirectionValidator.BeatMaxLength),
                    });
                }

                var carried = true;
                for (var i = 0; i < later.Count; i++)
                {
                    if (!heldBefore[i])
                    {
                        carried = false;
                    }
                    else if (carried)
                    {
                        later[i].Holding.Remove(prop);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
    }

    /// <summary>The props named, if the scene has them, in the order given.</summary>
    private static List<Guid> Held(SceneBrief brief, IEnumerable<string> names) =>
        names.Select(brief.PropNamed).OfType<PropRecord>().Where(brief.Props.Contains).Select(p => p.Id).Distinct().ToList();

    private static List<string> PropNames(SceneBrief brief, IEnumerable<Guid> ids) =>
        ids.Select(id => brief.Context.Story.Props.FirstOrDefault(p => p.Id == id)?.Name).OfType<string>().ToList();

    /// <summary>The character's look at the end of the nearest earlier scene that has one, with that scene.</summary>
    private static (SceneRecord Scene, Look Look)? PreviousLook(DirectedScene context, Guid characterId)
    {
        foreach (var scene in context.Earlier.Reverse())
        {
            if (Looks.Closing(scene, characterId) is { } look)
            {
                return (scene, look);
            }
        }

        return null;
    }

    private static (SceneRecord Scene, ScenePropStateRecord State)? PreviousState(DirectedScene context, Guid propId)
    {
        foreach (var scene in context.Earlier.Reverse())
        {
            if (scene.PropStates.FirstOrDefault(p => p.PropId == propId) is { } state)
            {
                return (scene, state);
            }
        }

        return null;
    }

    // ─── cues ──────────────────────────────────────────────────────────────

    private async Task CuesAsync(SceneBrief brief, DirectionDraft draft, Guid? onlyElement, bool replaceEdited, CancellationToken ct)
    {
        var lines = brief.Elements
            .Where(e => DirectionValidator.Spoken(e.Type))
            .Where(e => onlyElement is null || e.Id == onlyElement)
            .Where(e => replaceEdited || draft.Cues.FirstOrDefault(c => c.ElementId == e.Id) is not { Edited: true })
            .ToList();
        if (lines.Count == 0 && onlyElement is not null)
        {
            throw StoryErrors.NotFound(DirectionErrors.LineNotFound);
        }

        // Narration and action are read as written; only dialogue takes delivery tags.
        foreach (var line in lines.Where(e => e.Type != SceneElementTypeRecord.Dialogue))
        {
            var existing = draft.Cues.FirstOrDefault(c => c.ElementId == line.Id);
            Replace(draft, new CueRecord
            {
                Id = existing?.Id ?? Guid.Empty,
                ElementId = line.Id,
                Performance = SceneBrief.Plain(line.Text),
                GapBeforeMs = existing?.GapBeforeMs ?? 0,
                Interrupted = existing?.Interrupted ?? false,
                NarratorId = existing?.NarratorId,
            });
        }

        var dialogue = lines.Where(e => e.Type == SceneElementTypeRecord.Dialogue).ToList();
        if (dialogue.Count == 0)
        {
            return;
        }

        var focus = onlyElement is { } id
            ? $"Direct only line {brief.Label(id)}; the other lines are context.\n"
            : "Direct every dialogue line; narration and action are context.\n";
        var prompt = $$"""
            Decide how each line is delivered. {{focus}}
            Write the line back as "performance": its words exactly as given, with at most three delivery
            tags in square brackets placed where each takes effect: before the word where the delivery
            shifts mid-sentence, between sentences, or after the last word for a reaction (a sigh, a
            laugh). Put a tag at the start only when it sets the whole line. Honour parentheticals. Leave
            the words untagged when the speaker's baseline delivery fits: do not decorate every line.
            "gapBeforeMs" is the minimum silence before the line in milliseconds, more than 0 only when an
            action or a pause separates it from the line before; "interrupted" is true when the next line
            cuts this one off.

            Tags you may use, spelled exactly: {{string.Join(", ", brief.Context.References.DeliveryTags)}}

            Cast:
            {{brief.CastSheet()}}
            Scene {{brief.Heading()}}:
            {{brief.Script()}}
            Reply: {"cues":[{"line":"e3","performance":"I'm sorry. [sighs] I [hesitant] didn't mean it.","gapBeforeMs":0,"interrupted":false}]}
            """;

        using var json = await AskAsync(prompt, ct);
        var answers = ModelJson.Array(json.RootElement, "cues")
            .Select(c => (Element: brief.Element(ModelJson.Str(c, "line") ?? ModelJson.Str(c, "label")), Json: c))
            .Where(c => c.Element is not null)
            .GroupBy(c => c.Element!.Id)
            .ToDictionary(g => g.Key, g => g.First().Json);
        if (answers.Count == 0)
        {
            throw new ModelOutputException("No cues in the answer.");
        }

        var vocabulary = brief.Context.References.DeliveryTags;
        foreach (var line in dialogue)
        {
            var existing = draft.Cues.FirstOrDefault(c => c.ElementId == line.Id);
            var text = SceneBrief.Plain(line.Text);
            var cue = new CueRecord { Id = existing?.Id ?? Guid.Empty, ElementId = line.Id, Performance = text, NarratorId = existing?.NarratorId };
            if (answers.TryGetValue(line.Id, out var answer))
            {
                var marked = Performances.Mark(text, ModelJson.Str(answer, "performance") ?? text, t => Tag(t, vocabulary));
                if (marked is null && onlyElement is not null)
                {
                    throw new ModelOutputException("The answer changed the line's words.");
                }

                cue.Performance = marked ?? text;
                cue.GapBeforeMs = Math.Clamp(ModelJson.Int(answer, "gapBeforeMs") ?? 0, -5_000, 60_000);
                cue.Interrupted = ModelJson.Bool(answer, "interrupted");
            }
            else if (onlyElement is not null)
            {
                throw new ModelOutputException("The answer skipped the line that was asked for.");
            }

            Replace(draft, cue);
        }
    }

    private static void Replace(DirectionDraft draft, CueRecord cue)
    {
        draft.Cues.RemoveAll(c => c.ElementId == cue.ElementId);
        draft.Cues.Add(cue);
    }

    private static string? Tag(string? tag, IReadOnlyList<string> vocabulary) =>
        tag is null ? null : vocabulary.FirstOrDefault(t => LibraryNames.Same(t, tag.Trim('[', ']')));

    // ─── frames ────────────────────────────────────────────────────────────

    /// <summary>With <paramref name="directCuts"/> the author's frames keep their rows but the model directs them too.</summary>
    private async Task FramesAsync(SceneBrief brief, DirectionDraft draft, bool replaceEdited, bool directCuts, CancellationToken ct)
    {
        if (brief.Elements.Count == 0)
        {
            return;
        }

        var fixedFrames = replaceEdited ? [] : draft.Frames.Where(p => p.Edited).Select(p => (Frame: p, Range: Range(brief, p))).Where(p => p.Range is not null).ToList();
        var fixedRows = fixedFrames.Sum(p => p.Range!.Value.Last - p.Range.Value.First + 1);
        if (fixedRows >= brief.Elements.Count && !directCuts)
        {
            return;
        }

        var beats = new StringBuilder();
        foreach (var beat in Beats.Group(brief.Elements))
        {
            beats.Append(beat.First == beat.Last ? brief.Label(brief.Elements[beat.First].Id) : $"{brief.Label(brief.Elements[beat.First].Id)}–{brief.Label(brief.Elements[beat.Last].Id)}")
                .Append(beat.IsDialogue ? " (dialogue)" : " (action)").AppendLine();
        }

        var cuts = string.Join(", ", fixedFrames.Select(p => $"{brief.Label(brief.Elements[p.Range!.Value.First].Id)}–{brief.Label(brief.Elements[p.Range!.Value.Last].Id)}"));
        var kept = fixedFrames.Count == 0
            ? ""
            : directCuts
                ? $"The author already cut some rows into frames; answer one frame for each of them with exactly the same from and to ({cuts}), and split the other rows yourself.\n"
                : $"These rows are already covered by frames the author fixed, so leave them out: {cuts}.\n";
        var prompt = $$"""
            Split the scene into frames: the pictures a reader sees while the lines play. Each frame
            covers consecutive rows "from" one label "to" another (inclusive), in order, and together the
            frames cover every row once. A frame boundary falls where the picture would change, not
            where the text changes: a run of lines with no action between them can be one frame. Aim
            for one frame per natural beat; split a long exchange when the shot should change.
            {{kept}}
            For each frame give "shot" ({{string.Join("|", DirectionVocabulary.Shots)}}), camera "angle"
            ({{string.Join("|", DirectionVocabulary.Angles)}}) and "movement" ({{string.Join("|", DirectionVocabulary.Movements)}}),
            "visible", "characters" on screen with "screen" ({{string.Join("|", DirectionVocabulary.ScreenSides)}}),
            a short "pose" and "expression" and any "change" to their look this frame alone shows (empty
            when none), and "props" in view by name. Keep each character on the same screen side across
            consecutive frames. Most frames share an angle and stay static; change them for a reason.
            {{VisibleRule}}
            {{HoldingRule}}
            Natural beats, for guidance: {{beats}}
            Cast:
            {{brief.CastSheet()}}
            Looks:
            {{brief.LookSheet(draft.Appearances, draft.LookChanges)}}
            Props:
            {{brief.PropSheet()}}
            Places:
            {{brief.LocationSheet()}}
            Scene {{brief.Heading()}}:
            {{brief.Script()}}
            Reply: {"frames":[{"from":"e1","to":"e2","shot":"wide","angle":"eye level","movement":"static","visible":"…","characters":[{"name":"Name","screen":"left","pose":"…","expression":"…","change":""}],"props":["Name"]}]}
            """;

        using var json = await AskAsync(prompt, ct);
        var answered = new List<(int First, int Last, JsonElement Json)>();
        foreach (var frame in ModelJson.Array(json.RootElement, "frames"))
        {
            var first = brief.Index(ModelJson.Str(frame, "from") ?? ModelJson.Str(frame, "first"));
            var last = brief.Index(ModelJson.Str(frame, "to") ?? ModelJson.Str(frame, "last")) ?? first;
            if (first is null || last is null)
            {
                continue;
            }

            answered.Add((Math.Min(first.Value, last.Value), Math.Max(first.Value, last.Value), frame));
        }

        // Around fixed frames an empty answer is fair: the gaps are closed from them below.
        if (answered.Count == 0 && (fixedFrames.Count == 0 || directCuts))
        {
            throw new ModelOutputException("No frames in the answer.");
        }

        if (directCuts)
        {
            fixedFrames = fixedFrames.Select(f => (Frame: InCut(brief, answered, f.Frame, f.Range!.Value), f.Range)).ToList();
        }

        // Fixed frames keep their rows; model frames are clipped around them, then gaps are closed
        // by stretching the frame before each gap, so every row ends up in exactly one frame.
        var covered = new bool[brief.Elements.Count];
        var frames = new List<(int First, int Last, FrameRecord Frame)>();
        foreach (var (frame, range) in fixedFrames)
        {
            for (var i = range!.Value.First; i <= range.Value.Last; i++)
            {
                covered[i] = true;
            }

            frames.Add((range.Value.First, range.Value.Last, frame));
        }

        foreach (var (first, last, answer) in answered.OrderBy(a => a.First))
        {
            int? pieceStart = null;
            for (var i = first; i <= last + 1; i++)
            {
                var free = i <= last && !covered[i];
                if (free)
                {
                    pieceStart ??= i;
                    covered[i] = true;
                }
                else if (pieceStart is { } start)
                {
                    frames.Add((start, i - 1, FrameFrom(brief, answer, start, i - 1)));
                    pieceStart = null;
                }
            }
        }

        frames.Sort((a, b) => a.First.CompareTo(b.First));
        for (var i = 0; i < brief.Elements.Count; i++)
        {
            if (covered[i])
            {
                continue;
            }

            var before = frames.FindLastIndex(p => p.Last < i);
            if (before >= 0 && (i == 0 || frames[before].Last == i - 1 || !covered[i - 1]))
            {
                var stretched = frames[before];
                stretched.Last = i;
                stretched.Frame.LastElementId = brief.Elements[i].Id;
                frames[before] = stretched;
            }
            else
            {
                var next = frames.FindIndex(p => p.First > i);
                if (next >= 0)
                {
                    var pulled = frames[next];
                    for (var row = i; row < pulled.First; row++)
                    {
                        covered[row] = true;
                    }

                    pulled.First = i;
                    pulled.Frame.FirstElementId = brief.Elements[i].Id;
                    frames[next] = pulled;
                }
                else
                {
                    frames.Add((i, i, FrameFrom(brief, default, i, i)));
                }
            }

            covered[i] = true;
            frames.Sort((a, b) => a.First.CompareTo(b.First));
        }

        var directed = frames.Select(p => p.Frame).Where(p => !p.Edited).ToList();
        await VisibleAsync(brief, draft, directed, ct);
        if (draft.Shot is null || replaceEdited)
        {
            draft.Shot = Commonest(directed.Select(p => p.Shot));
        }

        if (draft.Angle is null || replaceEdited)
        {
            draft.Angle = Commonest(directed.Select(p => p.Angle));
        }

        if (draft.Movement is null || replaceEdited)
        {
            draft.Movement = Commonest(directed.Select(p => p.Movement));
        }

        foreach (var frame in directed)
        {
            AgainstScene(brief, draft, frame);
        }

        draft.Frames = frames.Select((p, index) =>
        {
            p.Frame.Position = index;
            return p.Frame;
        }).ToList();
    }

    /// <summary>The answer that overlaps the author's cut most, framed over the cut's own rows.</summary>
    private static FrameRecord InCut(
        SceneBrief brief, List<(int First, int Last, JsonElement Json)> answered, FrameRecord cut, (int First, int Last) range)
    {
        var best = answered
            .Select(a => (a.Json, Overlap: Math.Min(a.Last, range.Last) - Math.Max(a.First, range.First) + 1))
            .MaxBy(a => a.Overlap);
        if (best.Overlap <= 0)
        {
            return cut;
        }

        var directed = FrameFrom(brief, best.Json, range.First, range.Last);
        directed.Id = cut.Id;
        return directed;
    }

    private static string? Commonest(IEnumerable<string?> labels) =>
        labels.OfType<string>().GroupBy(l => l).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

    /// <summary>
    /// The model answers each frame in full; what matches the scene is stored as following it, so
    /// a change to the scene reaches the frame. Only the differences stay on the frame.
    /// </summary>
    private static void AgainstScene(SceneBrief brief, DirectionDraft draft, FrameRecord frame)
    {
        if (frame.Shot == draft.Shot)
        {
            frame.Shot = null;
        }

        if (frame.Angle == draft.Angle)
        {
            frame.Angle = null;
        }

        if (frame.Movement == draft.Movement)
        {
            frame.Movement = null;
        }

        // A held prop goes where its holder goes: in view while one is on screen, out of it otherwise.
        var last = brief.Elements.FindIndex(e => e.Id == frame.LastElementId);
        var held = new HashSet<Guid>();
        foreach (var character in brief.Cast)
        {
            held.UnionWith(Looks.At(draft.Appearances, draft.LookChanges, brief.Position, character.Id, last).Look.Holding);
        }

        foreach (var character in brief.Cast)
        {
            var sceneOn = draft.Appearances.FirstOrDefault(a => a.CharacterId == character.Id)?.OnScreen ?? true;
            var placement = frame.Placements.FirstOrDefault(p => p.CharacterId == character.Id);
            var on = placement?.OnScreen == true;
            if (on == sceneOn)
            {
                if (placement is not null)
                {
                    placement.OnScreen = null;
                }
            }
            else if (placement is null)
            {
                frame.Placements.Add(new FramePlacementRecord { CharacterId = character.Id, OnScreen = on });
            }
            else
            {
                placement.OnScreen = on;
            }
        }

        foreach (var prop in brief.Props)
        {
            // The model's list is often silent about what someone carries; the look decides.
            if (held.Contains(prop.Id))
            {
                frame.Props.RemoveAll(p => p.PropId == prop.Id);
                continue;
            }

            var sceneIn = draft.PropStates.FirstOrDefault(s => s.PropId == prop.Id)?.InView ?? true;
            var shown = frame.Props.FirstOrDefault(p => p.PropId == prop.Id);
            if ((shown is not null) == sceneIn)
            {
                frame.Props.RemoveAll(p => p.PropId == prop.Id);
            }
            else if (shown is null)
            {
                frame.Props.Add(new FramePropRecord { PropId = prop.Id, InView = false });
            }
        }
    }

    private async Task FrameAsync(SceneBrief brief, DirectionDraft draft, Guid frameId, CancellationToken ct)
    {
        var frame = draft.Frames.FirstOrDefault(p => p.Id == frameId) ?? throw StoryErrors.NotFound(DirectionErrors.FrameNotFound);
        var range = Range(brief, frame) ?? throw StoryErrors.NotFound(DirectionErrors.FrameNotFound);
        var prompt = $$"""
            One frame of the scene pictures rows {{brief.Label(brief.Elements[range.First].Id)}} to {{brief.Label(brief.Elements[range.Last].Id)}}.
            Decide its "shot" ({{string.Join("|", DirectionVocabulary.Shots)}}), camera "angle"
            ({{string.Join("|", DirectionVocabulary.Angles)}}) and "movement" ({{string.Join("|", DirectionVocabulary.Movements)}}),
            "visible", "characters" on screen with "screen"
            ({{string.Join("|", DirectionVocabulary.ScreenSides)}}), a short "pose" and "expression" and any "change"
            to their look this frame alone shows, and "props" in view by name. Keep characters on the
            sides they hold in the other frames, listed below.
            {{VisibleRule}}
            {{HoldingRule}}
            Other frames: {{string.Join("; ", draft.Frames.Where(p => p.Id != frameId).Select(p => Summary(brief, p)))}}

            Cast:
            {{brief.CastSheet()}}
            Looks:
            {{brief.LookSheet(draft.Appearances, draft.LookChanges)}}
            Props:
            {{brief.PropSheet()}}
            Places:
            {{brief.LocationSheet()}}
            Scene {{brief.Heading()}}:
            {{brief.Script()}}
            Reply: {"shot":"medium","angle":"eye level","movement":"static","visible":"…","characters":[{"name":"Name","screen":"left","pose":"…","expression":"…","change":""}],"props":["Name"]}
            """;

        using var json = await AskAsync(prompt, ct);
        var answer = json.RootElement.TryGetProperty("frame", out var inner) ? inner : json.RootElement;
        if (ModelJson.Str(answer, "shot") is null && Visible(answer) is null)
        {
            throw new ModelOutputException("No frame in the answer.");
        }

        var directed = FrameFrom(brief, answer, range.First, range.Last);
        await VisibleAsync(brief, draft, [directed], ct);
        AgainstScene(brief, draft, directed);
        directed.Id = frame.Id;
        directed.Position = frame.Position;
        draft.Frames[draft.Frames.IndexOf(frame)] = directed;
    }

    private static FrameRecord FrameFrom(SceneBrief brief, JsonElement answer, int first, int last)
    {
        var heading = brief.HeadingAt(first);
        var cast = brief.Cast;
        var frame = new FrameRecord
        {
            FirstElementId = brief.Elements[first].Id,
            LastElementId = brief.Elements[last].Id,
            LocationId = heading?.LocationId,
            TimeOfDay = heading?.TimeOfDay,
            Shot = DirectionVocabulary.Shots.FirstOrDefault(s => LibraryNames.Same(s, ModelJson.Str(answer, "shot") ?? "")) ?? "medium",
            Angle = DirectionVocabulary.Angles.FirstOrDefault(s => LibraryNames.Same(s, ModelJson.Str(answer, "angle") ?? "")) ?? "eye level",
            Movement = DirectionVocabulary.Movements.FirstOrDefault(s => LibraryNames.Same(s, ModelJson.Str(answer, "movement") ?? "")) ?? "static",
            Beat = Trim(Visible(answer) ?? "", DirectionValidator.BeatMaxLength),
        };
        foreach (var placement in ModelJson.Array(answer, "characters"))
        {
            var character = brief.CharacterNamed(ModelJson.Str(placement, "name") ?? ModelJson.Str(placement, "character"));
            if (character is null || !cast.Contains(character) || frame.Placements.Any(p => p.CharacterId == character.Id))
            {
                continue;
            }

            frame.Placements.Add(new FramePlacementRecord
            {
                CharacterId = character.Id,
                Screen = DirectionVocabulary.ScreenSides.FirstOrDefault(s => LibraryNames.Same(s, ModelJson.Str(placement, "screen") ?? "")) ?? "centre",
                Pose = Trim(ModelJson.Str(placement, "pose") ?? "", DirectionValidator.FieldMaxLength),
                Expression = Trim(ModelJson.Str(placement, "expression") ?? "", DirectionValidator.FieldMaxLength),
                OnScreen = true,
                AppearanceChange = Trim(ModelJson.Str(placement, "change") ?? "", DirectionValidator.BeatMaxLength),
            });
        }

        foreach (var name in ModelJson.Strings(answer, "props"))
        {
            var prop = brief.PropNamed(name);
            if (prop is not null && brief.Props.Contains(prop) && frame.Props.All(p => p.PropId != prop.Id))
            {
                frame.Props.Add(new FramePropRecord { PropId = prop.Id });
            }
        }

        return frame;
    }

    private static string? Visible(JsonElement answer) => ModelJson.Str(answer, "visible") ?? ModelJson.Str(answer, "beat");

    /// <summary>
    /// Frames whose "visible" is empty or leaves out someone on screen are written again in one call;
    /// anyone still left out is added from their placement, so every on-screen character is named.
    /// Runs before <see cref="AgainstScene"/>, while placements still say who is on screen.
    /// </summary>
    private async Task VisibleAsync(SceneBrief brief, DirectionDraft draft, IReadOnlyList<FrameRecord> frames, CancellationToken ct)
    {
        var thin = frames.Where(f => f.Beat.Length == 0 || Unnamed(brief, f).Count > 0).ToList();
        if (thin.Count == 0)
        {
            return;
        }

        var listing = new StringBuilder();
        for (var i = 0; i < thin.Count; i++)
        {
            listing.Append('f').Append(i + 1).Append(' ').AppendLine(Staging(brief, thin[i]));
        }

        var prompt = $$"""
            Write "visible" again for each frame below: what it has now leaves out someone on screen
            or says too little. Keep to the frame's rows, shot, who is on screen and the props in view.
            {{VisibleRule}}
            {{HoldingRule}}
            Frames:
            {{listing}}
            Cast:
            {{brief.CastSheet()}}
            Looks:
            {{brief.LookSheet(draft.Appearances, draft.LookChanges)}}
            Props:
            {{brief.PropSheet()}}
            Places:
            {{brief.LocationSheet()}}
            Scene {{brief.Heading()}}:
            {{brief.Script()}}
            Reply: {"frames":[{"frame":"f1","visible":"…"}]}
            """;

        try
        {
            using var json = await AskAsync(prompt, ct);
            foreach (var answer in ModelJson.Array(json.RootElement, "frames"))
            {
                var label = ModelJson.Str(answer, "frame")?.Trim().TrimStart('f', 'F');
                var text = Trim(Visible(answer) ?? "", DirectionValidator.BeatMaxLength);
                if (!int.TryParse(label, out var number) || number < 1 || number > thin.Count || text.Length == 0)
                {
                    continue;
                }

                var frame = thin[number - 1];
                if (Unnamed(brief, frame, text).Count <= Unnamed(brief, frame).Count)
                {
                    frame.Beat = text;
                }
            }
        }
        catch (ModelOutputException e)
        {
            logger.LogWarning("Rewriting what frames show failed ({Reason}); naming the cast from placements", e.Message);
        }

        foreach (var frame in thin)
        {
            var added = string.Join(" ", Unnamed(brief, frame).Select(p => Placed(brief, p)));
            frame.Beat = Trim($"{frame.Beat} {added}", DirectionValidator.BeatMaxLength);
        }
    }

    private static List<FramePlacementRecord> Unnamed(SceneBrief brief, FrameRecord frame, string? text = null) =>
        frame.Placements
            .Where(p => p.OnScreen == true && !Names(text ?? frame.Beat, CharacterName(brief, p.CharacterId)))
            .ToList();

    /// <summary>The full name, or any word of it, as a whole word: "Mara" names "Captain Mara Holt".</summary>
    private static bool Names(string text, string name) =>
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Prepend(name)
            .Any(part => part.Length > 2 && Regex.IsMatch(text, $@"\b{Regex.Escape(part)}\b", RegexOptions.IgnoreCase));

    private static string CharacterName(SceneBrief brief, Guid id) =>
        brief.Context.Story.Characters.FirstOrDefault(c => c.Id == id)?.Name ?? "";

    /// <summary>"Mara is screen left, leaning, tense."</summary>
    private static string Placed(SceneBrief brief, FramePlacementRecord placement)
    {
        var parts = new[] { placement.Screen.Length > 0 ? $"is screen {placement.Screen}" : "is in the picture", placement.Pose, placement.Expression }
            .Where(p => p.Length > 0);
        return $"{CharacterName(brief, placement.CharacterId)} {string.Join(", ", parts)}.";
    }

    /// <summary>A frame as the rewrite reads it: rows, shot, who is where, props, what it says now.</summary>
    private static string Staging(SceneBrief brief, FrameRecord frame)
    {
        var range = Range(brief, frame);
        var rows = range is null ? "?" : $"{brief.Label(brief.Elements[range.Value.First].Id)}–{brief.Label(brief.Elements[range.Value.Last].Id)}";
        var cast = frame.Placements.Where(p => p.OnScreen == true).Select(p =>
            $"{CharacterName(brief, p.CharacterId)} ({string.Join(", ", new[] { p.Screen, p.Pose, p.Expression }.Where(s => s.Length > 0))})").ToList();
        var props = frame.Props.Where(p => p.InView).Select(p => brief.Context.Story.Props.FirstOrDefault(r => r.Id == p.PropId)?.Name).OfType<string>().ToList();
        return $"rows {rows}, {frame.Shot ?? "scene"} shot; on screen: {(cast.Count == 0 ? "nobody" : string.Join(", ", cast))}; "
               + $"props in view: {(props.Count == 0 ? "none" : string.Join(", ", props))}; has now: \"{frame.Beat}\"";
    }

    private static (int First, int Last)? Range(SceneBrief brief, FrameRecord frame)
    {
        var first = brief.Elements.FindIndex(e => e.Id == frame.FirstElementId);
        var last = brief.Elements.FindIndex(e => e.Id == frame.LastElementId);
        return first < 0 || last < 0 ? null : (Math.Min(first, last), Math.Max(first, last));
    }

    private static string Summary(SceneBrief brief, FrameRecord frame)
    {
        var range = Range(brief, frame);
        var rows = range is null ? "?" : $"{brief.Label(brief.Elements[range.Value.First].Id)}–{brief.Label(brief.Elements[range.Value.Last].Id)}";
        var placements = string.Join(", ", frame.Placements.Where(p => p.Screen.Length > 0).Select(p =>
            $"{brief.Context.Story.Characters.FirstOrDefault(c => c.Id == p.CharacterId)?.Name ?? "?"} {p.Screen}"));
        return $"{rows} {frame.Shot ?? "scene shot"} [{placements}]";
    }

    // ─── the model ─────────────────────────────────────────────────────────

    /// <summary>One retry with a nudge: a small model's first answer is sometimes prose around the JSON.</summary>
    private async Task<JsonDocument> AskAsync(string prompt, CancellationToken ct)
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

    private static Guid Id(string id, string notFound) => StoryIds.Existing(id, notFound);

    private static string Trim(string text, int max)
    {
        var collapsed = Performances.Collapse(text);
        return collapsed.Length <= max ? collapsed : collapsed[..max];
    }

    private static string? Reason(string? reason)
    {
        var trimmed = reason?.Trim();
        return string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase)
            ? null
            : Trim(trimmed, DirectionValidator.BeatMaxLength);
    }

    private static List<string> Short(List<string> values) =>
        values.Select(v => Trim(v, DirectionValidator.FieldMaxLength)).Where(v => v.Length > 0).Distinct().Take(8).ToList();

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
