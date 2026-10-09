using System.Text;
using System.Text.Json;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.DocumentImport.Screenplay.Models;
using CharacterWardrobeRecord = ProtoFast.Data.ThePlot.Entities.CharacterWardrobe;
using CueRecord = ProtoFast.Data.ThePlot.Entities.Cue;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using DirectionVocabulary = ProtoFast.Data.ThePlot.Entities.DirectionVocabulary;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;
using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
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

    public async Task<DirectionDraft> DirectAsync(DirectedScene context, DirectRequest request, CancellationToken ct)
    {
        var brief = new SceneBrief(context);
        var draft = DirectionDraft.From(context.Scene);
        var replace = request.ReplaceEdited;
        switch (request.TargetCase)
        {
            case DirectRequest.TargetOneofCase.Scene:
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
    /// A look carries forward: on the same story day a character wears what they wore in their last
    /// scene unless a line shows a change, and state (a bandage, a stain) persists until the text
    /// removes it. The model is told the previous look, the sheet's wardrobes and the day, and may
    /// name a new wardrobe only when the text describes clothing the sheet has not got.
    /// </summary>
    private async Task ContinuityAsync(
        SceneBrief brief, DirectionDraft draft, Guid? onlyCharacter, Guid? onlyProp, bool replaceEdited, CancellationToken ct)
    {
        var cast = brief.Cast.Where(c => onlyCharacter is null || c.Id == onlyCharacter).ToList();
        var props = brief.Props.Where(p => onlyProp is null || p.Id == onlyProp).ToList();
        if (onlyProp is not null)
        {
            cast = [];
        }

        if (onlyCharacter is not null)
        {
            props = [];
        }

        if (cast.Count == 0 && props.Count == 0 && (onlyCharacter is not null || onlyProp is not null || brief.Locations.Count == 0))
        {
            return;
        }

        var previousLooks = cast.ToDictionary(c => c.Id, c => PreviousLook(brief.Context, c.Id));
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
                looks.Append("  previous look: scene \"").Append(scene.Title).Append("\" (day ")
                    .Append(scene.StoryDay?.ToString() ?? "unknown").Append(", ").Append(when)
                    .Append("): wardrobe \"")
                    .Append(character.Wardrobes.FirstOrDefault(w => w.Id == look.WardrobeId)?.Label ?? "none")
                    .Append("\", wardrobe change ").Append(Json(look.WardrobeChange))
                    .Append(", appearance change ").AppendLine(Json(look.AppearanceChange));
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
            forward unchanged: the same wardrobe and the same state. Change it only when a line of this
            scene shows it (they changed clothes, got wet, were hurt, washed up), and then name that line.
            On the same story day the wardrobe stays unless a line says otherwise. On a later day the
            wardrobe may differ, but lasting state (a bandage, a scar, a torn sleeve) still carries over
            unless the text says it is gone. A first appearance takes the wardrobe on the sheet that fits
            the scene best.

            For each character: "present" is false when they are only talked about or remembered and
            never physically in the scene; "wardrobe" is one of the labels on their sheet, spelled
            exactly, or null when the sheet has none that fits; "wardrobeChange" is a short phrase for
            what is different about that outfit here ("hood up, soaked through"), empty when nothing;
            "appearanceChange" is a short phrase for what shows on them beyond their sheet (a bandage,
            wet hair), empty when nothing; "changeReason" says why the look differs from the previous
            one, or null when it does not differ; "changeLine" is the label (e3, …) of the line that
            causes the change, or null.
            Only when the text of this scene describes clothing the sheet has not got, add "newWardrobe"
            with a short "label" and a "description" taken from the text; otherwise leave it null.
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
            Props:
            {{(states.Length == 0 ? "(none)\n" : states.ToString())}}
            Locations:
            {{(locations.Length == 0 ? "(none)\n" : locations.ToString())}}
            Reply: {"looks":[{"name":"Name","present":true,"wardrobe":"label or null","newWardrobe":null,"wardrobeChange":"","appearanceChange":"","changeReason":null,"changeLine":null}],"props":[{"name":"Name","state":[],"changeReason":null,"changeLine":null}],"locations":[{"name":"Name","change":""}]}
            """;

        using var json = await AskAsync(prompt, ct);
        var answeredLooks = ModelJson.Array(json.RootElement, "looks")
            .Select(l => (Character: brief.CharacterNamed(ModelJson.Str(l, "name") ?? ModelJson.Str(l, "character")), Json: l))
            .Where(l => l.Character is not null)
            .ToDictionary(l => l.Character!.Id, l => l.Json);
        var answeredStates = ModelJson.Array(json.RootElement, "props")
            .Select(p => (Prop: brief.PropNamed(ModelJson.Str(p, "name") ?? ModelJson.Str(p, "prop")), Json: p))
            .Where(p => p.Prop is not null)
            .ToDictionary(p => p.Prop!.Id, p => p.Json);
        if (answeredLooks.Count == 0 && answeredStates.Count == 0 && cast.Count + props.Count > 0)
        {
            throw new ModelOutputException("No looks or prop states in the answer.");
        }

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
            };
            if (answeredLooks.TryGetValue(character.Id, out var answer))
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
            }

            draft.Appearances.RemoveAll(a => a.CharacterId == character.Id);
            draft.Appearances.Add(look);
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

    /// <summary>The character's look in the nearest earlier scene that has one, with that scene.</summary>
    private static (SceneRecord Scene, SceneAppearanceRecord Look)? PreviousLook(DirectedScene context, Guid characterId)
    {
        foreach (var scene in context.Earlier.Reverse())
        {
            if (scene.Appearances.FirstOrDefault(a => a.CharacterId == characterId) is { } look)
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
            .ToList();
        if (lines.Count == 0)
        {
            if (onlyElement is not null)
            {
                throw StoryErrors.NotFound(DirectionErrors.LineNotFound);
            }

            return;
        }

        var focus = onlyElement is { } id
            ? $"Direct only line {brief.Label(id)}; the other lines are context.\n"
            : "Direct every dialogue, narration and action line.\n";
        var prompt = $$"""
            Decide how each line is delivered. {{focus}}
            For a line: "tags" are up to two delivery tags that set the whole line (emotion or manner);
            "inline" places at most one more tag before a word, by 1-based word index, where the delivery
            shifts mid-line or a non-verbal (a sigh, a pause) belongs. Honour parentheticals. Leave "tags"
            empty when the speaker's baseline delivery fits: do not decorate every line. "gapBeforeMs" is
            the minimum silence before the line in milliseconds, more than 0 only when an action or a
            pause separates it from the line before; "interrupted" is true when the next line cuts this
            one off. Narration and action lines are read aloud by a narrator, as written; tag them only
            when the pace or the mood calls for it (slowly, tense).

            Tags you may use, spelled exactly: {{string.Join(", ", brief.Context.References.DeliveryTags)}}

            Cast:
            {{brief.CastSheet()}}
            Scene {{brief.Heading()}}:
            {{brief.Script()}}
            Reply: {"cues":[{"line":"e3","tags":["sad"],"inline":[{"beforeWord":4,"tag":"sighs"}],"gapBeforeMs":0,"interrupted":false}]}
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
        foreach (var line in lines)
        {
            var existing = draft.Cues.FirstOrDefault(c => c.ElementId == line.Id);
            if (existing is { Edited: true } && !replaceEdited)
            {
                continue;
            }

            var text = SceneBrief.Plain(line.Text);
            var cue = new CueRecord { Id = existing?.Id ?? Guid.Empty, ElementId = line.Id, Performance = text, NarratorId = existing?.NarratorId };
            if (answers.TryGetValue(line.Id, out var answer))
            {
                var leading = ModelJson.Strings(answer, "tags").Select(t => Tag(t, vocabulary)).OfType<string>();
                var inline = ModelJson.Array(answer, "inline")
                    .Select(i => (Word: ModelJson.Int(i, "beforeWord") ?? 1, Tag: Tag(ModelJson.Str(i, "tag"), vocabulary)))
                    .Where(i => i.Tag is not null)
                    .Select(i => (Math.Max(i.Word - 1, 0), i.Tag!));
                cue.Performance = Performances.Assemble(text, leading, inline);
                cue.GapBeforeMs = Math.Clamp(ModelJson.Int(answer, "gapBeforeMs") ?? 0, -5_000, 60_000);
                cue.Interrupted = ModelJson.Bool(answer, "interrupted");
            }
            else if (onlyElement is not null)
            {
                throw new ModelOutputException("The answer skipped the line that was asked for.");
            }

            draft.Cues.RemoveAll(c => c.ElementId == line.Id);
            draft.Cues.Add(cue);
        }
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
            "beat" (one sentence of what is visible), "characters" on screen with "screen" ({{string.Join("|", DirectionVocabulary.ScreenSides)}}),
            a short "pose" and "expression" and any "change" to their look this frame alone shows (empty
            when none), and "props" in view by name. Keep each character on the same screen side across
            consecutive frames. Most frames share an angle and stay static; change them for a reason.

            Natural beats, for guidance: {{beats}}
            Cast:
            {{brief.CastSheet()}}
            Props:
            {{brief.PropSheet()}}
            Scene {{brief.Heading()}}:
            {{brief.Script()}}
            Reply: {"frames":[{"from":"e1","to":"e2","shot":"wide","angle":"eye level","movement":"static","beat":"…","characters":[{"name":"Name","screen":"left","pose":"…","expression":"…","change":""}],"props":["Name"]}]}
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
            "beat" (one sentence of what is visible), "characters" on screen with "screen"
            ({{string.Join("|", DirectionVocabulary.ScreenSides)}}), a short "pose" and "expression" and any "change"
            to their look this frame alone shows, and "props" in view by name. Keep characters on the
            sides they hold in the other frames, listed below.

            Other frames: {{string.Join("; ", draft.Frames.Where(p => p.Id != frameId).Select(p => Summary(brief, p)))}}

            Cast:
            {{brief.CastSheet()}}
            Props:
            {{brief.PropSheet()}}
            Scene {{brief.Heading()}}:
            {{brief.Script()}}
            Reply: {"shot":"medium","angle":"eye level","movement":"static","beat":"…","characters":[{"name":"Name","screen":"left","pose":"…","expression":"…","change":""}],"props":["Name"]}
            """;

        using var json = await AskAsync(prompt, ct);
        var answer = json.RootElement.TryGetProperty("frame", out var inner) ? inner : json.RootElement;
        if (ModelJson.Str(answer, "shot") is null && ModelJson.Str(answer, "beat") is null)
        {
            throw new ModelOutputException("No frame in the answer.");
        }

        var directed = FrameFrom(brief, answer, range.First, range.Last);
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
            Beat = Trim(ModelJson.Str(answer, "beat") ?? "", DirectionValidator.BeatMaxLength),
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
