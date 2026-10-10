using System.Text;
using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Screenplays;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using LocationVariationRecord = ProtoFast.Data.ThePlot.Entities.LocationVariation;
using LocationSettingRecord = ProtoFast.Data.ThePlot.Entities.LocationSetting;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using ExtraRecord = ProtoFast.Data.ThePlot.Entities.Extra;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// Image prompts are compiled from the records, never written by a model, so an entity is described
/// in the same words in every picture of the story (story-publishing.md §5.2 I3). Identity comes from
/// the sheet; what changes comes from the scene and the frame.
/// </summary>
public static class ImagePrompts
{
    private const string NoText = "No text, captions, subtitles, labels or watermarks anywhere in the image.";

    /// <summary>A bigger group's reference shows this many of them, standing for the rest.</summary>
    private const int LineUp = 8;

    public static string Character(CharacterRecord character, string style)
    {
        var text = new StringBuilder();
        text.AppendLine(style);
        text.AppendLine($"A character reference sheet of {character.Name}, a {character.Kind.ToLowerInvariant()}: the same figure twice, full body, front view and three-quarter view side by side, standing in a neutral pose against a plain light-grey studio backdrop, in soft even light. Nobody else in the image.");
        AppendIdentity(text, character);
        if (character.Wardrobes.OrderBy(w => w.Position).FirstOrDefault() is { } wardrobe)
        {
            text.AppendLine($"Wearing {Wardrobe(wardrobe.Label, wardrobe.Description)}.");
        }

        AppendLine(text, character.Description);
        text.Append(NoText);
        return text.ToString();
    }

    /// <summary>
    /// The character's reference sheet in a look other than the library's, so every frame in that
    /// look draws them, their clothes and what they carry from one picture.
    /// </summary>
    public static string Look(CharacterRecord character, Look look, IReadOnlyList<PropRecord> holding, string style)
    {
        var text = new StringBuilder();
        text.AppendLine(style);
        text.AppendLine($"A character reference sheet of {character.Name}, a {character.Kind.ToLowerInvariant()}, as they look at this point of the story: the same figure twice, full body, front view and three-quarter view side by side, standing in a neutral pose against a plain light-grey studio backdrop, in soft even light. Nobody else in the image.");
        AppendIdentity(text, character);
        var wardrobe = character.Wardrobes.FirstOrDefault(w => w.Id == look.WardrobeId) ?? character.Wardrobes.OrderBy(w => w.Position).FirstOrDefault();
        var wearing = wardrobe is null ? "" : Wardrobe(wardrobe.Label, wardrobe.Description);
        if (wearing.Length > 0 || look.WardrobeChange.Trim().Length > 0)
        {
            text.AppendLine($"Wearing {string.Join("; ", new[] { wearing, look.WardrobeChange.Trim() }.Where(p => p.Length > 0))}.");
        }

        if (look.AppearanceChange.Trim() is { Length: > 0 } appearance)
        {
            text.AppendLine($"Right now: {appearance}");
        }

        if (holding.Count > 0)
        {
            text.AppendLine($"Carrying, the same way in both views: {Carried(holding)}");
        }

        AppendLine(text, character.Description);
        text.Append(NoText);
        return text.ToString();
    }

    public static string Location(LocationRecord location, string style)
    {
        var text = new StringBuilder();
        text.AppendLine(style);
        text.AppendLine($"An establishing wide shot of {location.Name}, {Setting(location.Setting)}, empty of people.");
        AppendPlace(text, location);
        if (location.Appearance?.Lighting.FirstOrDefault(l => l.Description.Length > 0) is { } light)
        {
            text.AppendLine($"Light ({light.TimeOfDay.ToLowerInvariant()}): {light.Description.Trim()}");
        }

        AppendLine(text, location.Description);
        text.Append(NoText);
        return text.ToString();
    }

    /// <summary>
    /// A part of the place, empty, seen from where the camera stands there. With <paramref name="fromPlate"/>
    /// it is drawn from image 1, the location's establishing shot, so both pictures show one place.
    /// </summary>
    public static string LocationVariation(LocationRecord location, LocationVariationRecord variation, string style, bool fromPlate)
    {
        var text = new StringBuilder();
        text.AppendLine(style);
        text.AppendLine(fromPlate
            ? $"Image 1 is the establishing shot of {location.Name}. Picture the same place, empty of people, from another spot in it: "
              + "the camera has moved, so compose this picture afresh from where it stands now. Keep the walls, floor, ceiling, "
              + "fittings, materials, colours, wear and light exactly as image 1 shows them, and build what image 1 doesn't show "
              + "in the same way, following the layout below."
            : $"A wide shot of one part of {location.Name}, {Setting(location.Setting)}, empty of people.");
        text.AppendLine($"This part of the place: {variation.Name.Trim()}. {variation.Description.Trim()}".TrimEnd());
        AppendPlace(text, location, plan: true);
        if (location.Appearance?.Lighting.FirstOrDefault(l => l.Description.Length > 0) is { } light)
        {
            text.AppendLine($"Light ({light.TimeOfDay.ToLowerInvariant()}): {light.Description.Trim()}");
        }

        if (!fromPlate)
        {
            AppendLine(text, location.Description);
        }

        text.Append(NoText);
        return text.ToString();
    }

    public const string LocationVariationCaption = "Image 1: the establishing shot of the place, empty. Take the place from it, not its camera.";

    public static string Prop(PropRecord prop, string style)
    {
        var text = new StringBuilder();
        text.AppendLine(style);
        text.AppendLine($"A reference photograph of {prop.Name}, alone and whole, centred against a plain neutral backdrop in soft even light. No people, no hands.");
        AppendLine(text, prop.Description);
        text.Append(NoText);
        return text.ToString();
    }

    /// <summary>A group is drawn as the group, so the same people stand behind the cast in every scene.</summary>
    public static string Extra(ExtraRecord extra, string style)
    {
        const string backdrop = "against a plain light-grey studio backdrop, in soft even light";
        var text = new StringBuilder();
        text.AppendLine(style);
        text.AppendLine(extra.Headcount switch
        {
            1 => $"A reference picture of {extra.Name}, one uncast person in the background of this production: that one person alone, full body, standing in a neutral pose {backdrop}. Nobody else in the image.",
            <= LineUp and { } count => $"A reference picture of {extra.Name}, a group of {count} uncast people in the background of this production: exactly {count} people, full body, standing in neutral poses side by side {backdrop}. Nobody else in the image.",
            { } count => $"A reference picture of {extra.Name}, a crowd of {count} uncast people in the background of this production: a line-up of {LineUp} of them that shows the crowd's range of looks, full body, standing in neutral poses side by side {backdrop}. Nobody else in the image.",
            null => $"A reference picture of {extra.Name}, uncast people in the background of this production: exactly the people the description says and nobody else, and if it describes one person, that one person alone; full body, standing in neutral poses {backdrop}.",
        });
        AppendLine(text, extra.Description);
        if (!string.IsNullOrWhiteSpace(extra.Wardrobe))
        {
            text.AppendLine($"Wearing: {extra.Wardrobe.Trim()}");
        }

        text.Append(NoText);
        return text.ToString();
    }

    /// <summary>Style, place and light, each character, the extras, the props, then the shot and the action.</summary>
    public static string Frame(FrameContents frame, string style)
    {
        var text = new StringBuilder();
        text.AppendLine(style);

        if (frame.Location is { } location)
        {
            text.AppendLine($"Place: {location.Name}, {Setting(location.Setting)}.");
            AppendPlace(text, location, plan: true);
            AppendPart(text, frame);
        }

        var timeOfDay = frame.TimeOfDay?.Trim() ?? "";
        var light = frame.Location?.Appearance?.Lighting
            .FirstOrDefault(l => string.Equals(l.TimeOfDay.Trim(), timeOfDay, StringComparison.OrdinalIgnoreCase));
        if (light is not null && light.Description.Length > 0)
        {
            text.AppendLine($"Light ({timeOfDay.ToLowerInvariant()}): {light.Description.Trim()}");
        }
        else if (timeOfDay.Length > 0)
        {
            text.AppendLine($"Time of day: {timeOfDay.ToLowerInvariant()}.");
        }

        if (frame.LocationChange.Length > 0)
        {
            text.AppendLine($"Right now: {frame.LocationChange}");
        }

        foreach (var shown in frame.OnScreen)
        {
            text.AppendLine();
            text.AppendLine($"{shown.Character.Name} ({shown.Character.Kind.ToLowerInvariant()}):");
            AppendIdentity(text, shown.Character);
            AppendWearing(text, shown, frame.InView);

            var staging = Staging(shown.Screen, shown.Depth, shown.Mark, shown.Facing, shown.Moving, shown.Looking, shown.Pose)
                .Append(shown.Expression.Length > 0 ? $"expression: {shown.Expression}" : "")
                .Where(p => p.Length > 0)
                .ToList();
            if (staging.Count > 0)
            {
                text.AppendLine($"Staging: {string.Join("; ", staging)}.");
            }
        }

        foreach (var shown in frame.Extras)
        {
            text.AppendLine();
            AppendExtra(text, shown.Extra);
            var staging = Staging(shown.Screen, shown.Depth, shown.Mark, shown.Facing, shown.Moving, shown.Looking, shown.Pose)
                .Where(p => p.Length > 0).ToList();
            if (staging.Count > 0)
            {
                text.AppendLine($"Staging: {string.Join("; ", staging)}.");
            }
        }

        if (frame.InView.Any(p => p.HeldBy is null))
        {
            text.AppendLine();
            foreach (var prop in frame.InView.Where(p => p.HeldBy is null))
            {
                var state = prop.State.Count > 0 ? $" ({string.Join(", ", prop.State)})" : "";
                text.AppendLine($"{prop.Prop.Name}{state}{(string.IsNullOrWhiteSpace(prop.Prop.Description) ? "" : $": {prop.Prop.Description.Trim()}")}");
                if (Where(prop) is { Length: > 0 } where)
                {
                    text.AppendLine($"Where it is: {where}.");
                }
            }
        }

        text.AppendLine();
        AppendCamera(text, frame);
        if (frame.OnScreen.Count + frame.Extras.Count > 0)
        {
            text.AppendLine("Left and right mean the viewer's left and right in the picture, never a person's own.");
        }

        if (frame.Frame.Beat.Trim() is { Length: > 0 } beat)
        {
            text.AppendLine($"In the picture: {beat}");
        }

        AppendPeople(text, frame);
        text.Append(NoText);
        return text.ToString();
    }

    private static void AppendCamera(StringBuilder text, FrameContents frame)
    {
        var camera = new[]
        {
            frame.Shot is null ? "" : $"{frame.Shot} shot",
            frame.Angle is null ? "" : $"{frame.Angle} angle",
        }.Where(p => p.Length > 0).ToList();
        if (frame.Frame.Camera.Trim().TrimEnd('.') is { Length: > 0 } position)
        {
            camera.Add(position);
        }

        if (camera.Count > 0)
        {
            text.AppendLine($"Camera: {string.Join(", ", camera)}.");
        }
    }

    /// <summary>Who is left out, then the headcount, so the model neither drops nor adds anyone.</summary>
    /// <param name="named">Already said to be out of the picture.</param>
    private static void AppendPeople(StringBuilder text, FrameContents frame, IReadOnlyList<string>? named = null)
    {
        if (frame.OffScreen.Select(c => c.Name).Except(named ?? []).ToList() is { Count: > 0 } off)
        {
            text.AppendLine($"Not in the picture: {string.Join(", ", off)}.");
        }

        text.AppendLine((frame.OnScreen.Count, frame.Extras.Count) switch
        {
            (0, 0) => "No people in the picture.",
            (0, _) => "No one in the picture but the extras described.",
            (var count, 0) => $"Exactly {People(count)} in the picture.",
            var (count, _) => $"Exactly {People(count)} in the picture besides the extras described.",
        });
    }

    /// <summary>A variation is the previous frame's picture edited, so it says only what changes.</summary>
    public static string Variation(FrameContents frame) =>
        $"""
        Edit this picture: {frame.Frame.Variation.Trim().TrimEnd('.')}.
        Change nothing else. Keep the same people with the same faces, clothes and whatever they carry,
        the same place and light, the same camera, framing and composition, and everyone and everything
        the change doesn't move exactly where it is.
        {NoText}
        """;

    public const string RefineCaption = "The picture to edit.";

    /// <summary>The author's edit of an existing picture: only what they ask changes.</summary>
    public static string Refine(string change) =>
        $"""
        Edit this picture: {change.TrimEnd('.')}.
        Change nothing else. Keep the same people with the same faces, clothes and whatever they carry,
        the same place and light, the same style, camera, framing and composition, and everything the
        change doesn't touch exactly as it is.
        {NoText}
        """;

    public const string RefineWithReferenceCaption = "Image 1: the picture to edit.";

    public const string RefineReferenceCaption =
        "Image 2: a reference the author picked for the change. Take from it only what the change asks for.";

    /// <summary>The author's edit of image 1, taking what it needs from image 2: only what they ask changes.</summary>
    public static string RefineWithReference(string change) =>
        $"""
        Edit image 1: {change.TrimEnd('.')}. Use image 2 as the reference for the change.
        Change nothing else in image 1. Keep the same people with the same faces, clothes and whatever
        they carry, the same place and light, the same style, camera, framing and composition, and
        everything the change doesn't touch exactly as it is. Image 2 is not the picture to make.
        {NoText}
        """;

    /// <summary>
    /// A frame based off the previous one whose camera moves: image 1 carries the place, its light and
    /// whoever stays; only what comes into view is described in full, and everyone is staged afresh.
    /// </summary>
    public static string Continuation(FrameContents frame, string style)
    {
        var previous = frame.VariationOf!;
        var text = new StringBuilder();
        text.AppendLine(style);
        text.AppendLine(frame.SamePart(previous)
            ? $"The next picture of the same scene, a moment after image 1{(frame.Location is { } at ? $", still in {at.Name}" : "")}. "
              + "The camera has moved, so compose this picture afresh from where it stands now. Keep the place, its light and "
              + "everyone still in the picture exactly as image 1 shows them: faces, bodies, clothes and what they carry."
            : $"The next picture of the same scene, a moment after image 1, at another part of {frame.Location?.Name ?? "the place"}. "
              + "The camera has moved there, so compose this picture afresh from where it stands now. Keep everyone still in the "
              + "picture exactly as image 1 shows them: faces, bodies, clothes and what they carry. Take nothing of the place from "
              + "image 1's background: draw the place around them as the picture of this part of it shows.");
        if (frame.Location is { } location)
        {
            AppendPlace(text, location, plan: true);
            AppendPart(text, frame);
        }

        if (frame.LocationChange.Length > 0)
        {
            text.AppendLine($"Right now: {frame.LocationChange}");
        }

        text.AppendLine();
        AppendCamera(text, frame);
        text.AppendLine($"What changes: {frame.Frame.Variation.Trim()}");
        var gone = frame.GoneSince(previous.Pictured);
        if (gone.Count > 0)
        {
            text.AppendLine($"No longer in the picture: {string.Join(", ", gone)}.");
        }

        var arrived = frame.NewSince(previous.Pictured);
        var newcomers = frame.OnScreen.Where(c => arrived.Contains(c.Character.Id)).ToList();
        var newExtras = frame.Extras.Where(e => arrived.Contains(e.Extra.Id)).ToList();
        var newProps = frame.InView.Where(p => arrived.Contains(p.Prop.Id) && p.HeldBy is null).ToList();
        if (newcomers.Count + newExtras.Count + newProps.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("New in the picture, not in image 1:");
            foreach (var shown in newcomers)
            {
                text.AppendLine($"{shown.Character.Name} ({shown.Character.Kind.ToLowerInvariant()}):");
                AppendIdentity(text, shown.Character);
                AppendWearing(text, shown);
            }

            foreach (var shown in newExtras)
            {
                AppendExtra(text, shown.Extra);
            }

            foreach (var prop in newProps)
            {
                text.AppendLine($"{prop.Prop.Name}{(string.IsNullOrWhiteSpace(prop.Prop.Description) ? "" : $": {prop.Prop.Description.Trim()}")}");
            }
        }

        var staging = frame.OnScreen
            .Select(c => (c.Character.Name, Parts: Staging(c.Screen, c.Depth, c.Mark, c.Facing, c.Moving, c.Looking, c.Pose)
                .Append(c.Expression.Length > 0 ? $"expression: {c.Expression}" : "")
                .Append(c.Holding.Count > 0 ? $"carrying {string.Join(" and ", c.Holding.Select(p => p.Name))}" : "")))
            .Concat(frame.Extras.Select(e => (e.Extra.Name, Parts: Staging(e.Screen, e.Depth, e.Mark, e.Facing, e.Moving, e.Looking, e.Pose))))
            .Concat(frame.InView.Where(p => p.HeldBy is null).Select(p => (p.Prop.Name, Parts: (IEnumerable<string>)[Where(p)])))
            .Select(s => (s.Name, Parts: s.Parts.Where(p => p.Length > 0).ToList()))
            .Where(s => s.Parts.Count > 0)
            .ToList();
        if (staging.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Where everyone is now, as the viewer sees the picture (left and right are the viewer's, never a person's own):");
            foreach (var (name, parts) in staging)
            {
                text.AppendLine($"{name}: {string.Join("; ", parts)}.");
            }
        }

        AppendPeople(text, frame, gone);
        text.Append(NoText);
        return text.ToString();
    }

    /// <param name="frame">The frame being pictured, whose camera moves when it is not an edit of <paramref name="previous"/>.</param>
    public static string VariationCaption(FrameContents frame, FrameContents previous) => !frame.MovesCamera
        ? $"Frame {previous.Number} of this scene: the picture to edit."
        : frame.SamePart(previous)
            ? $"Image 1: Frame {previous.Number} of this scene, the picture just before this one. The camera has moved since: "
              + "take the place, its light and the people who stay from it, not its composition."
            : $"Image 1: Frame {previous.Number} of this scene, the picture just before this one, at another part of the place. "
              + "Take only the people who stay from it, as they look and what they wear and carry, never its background or composition.";

    /// <summary>"Where in the place: Elevator end. The far end of the corridor…"</summary>
    private static void AppendPart(StringBuilder text, FrameContents frame)
    {
        if (frame.LocationVariation is { } part)
        {
            text.AppendLine($"Where in the place this picture is: {part.Name.Trim()}. {part.Description.Trim()}".TrimEnd());
        }
    }

    private static void AppendWearing(StringBuilder text, FrameCharacter shown, IReadOnlyList<FramePropView>? inView = null)
    {
        if (shown.Wardrobe is not null || shown.WardrobeChange.Length > 0)
        {
            var wearing = shown.Wardrobe is null ? "" : Wardrobe(shown.Wardrobe.Label, shown.Wardrobe.Description);
            text.AppendLine($"Wearing {string.Join("; ", new[] { wearing, shown.WardrobeChange }.Where(p => p.Length > 0))}.");
        }

        if (shown.AppearanceChange.Length > 0)
        {
            text.AppendLine($"In this moment: {shown.AppearanceChange}");
        }

        if (shown.Holding.Count > 0)
        {
            text.AppendLine($"Carrying: {Carried(shown.Holding, inView)}");
        }
    }

    private static void AppendExtra(StringBuilder text, ExtraRecord extra)
    {
        text.AppendLine(extra.Headcount is { } count
            ? $"{extra.Name} (extras, in the background, {Headcount.Words(count)}):"
            : $"{extra.Name} (extras, in the background):");
        AppendLine(text, extra.Description);
        if (!string.IsNullOrWhiteSpace(extra.Wardrobe))
        {
            text.AppendLine($"Wearing: {extra.Wardrobe.Trim()}");
        }
    }

    /// <summary>"frame left, in the foreground, at the nurse station", "turned three-quarters toward screen right", …</summary>
    private static IEnumerable<string> Staging(string screen, string depth, string mark, string facing, string moving, string looking, string pose)
    {
        yield return Position(screen, depth, mark);
        yield return facing switch
        {
            "" => "",
            "toward camera" => "body facing the camera",
            "away from camera" => "back to the camera",
            "screen left" or "screen right" => $"in profile, facing {facing}",
            _ when facing.StartsWith("back ", StringComparison.Ordinal) =>
                $"back to the camera, body turned three-quarters toward screen {facing["back three-quarter ".Length..]}",
            _ => $"face toward the camera, body turned three-quarters toward screen {facing["three-quarter ".Length..]}",
        };
        yield return Moving(moving);
        yield return looking.Trim() is { Length: > 0 } at ? $"looking at {at}" : "";
        yield return pose;
    }

    private static string Where(FramePropView prop) => Position(prop.Screen, prop.Depth, prop.Mark);

    private static string Position(string screen, string depth, string mark) =>
        string.Join(", ", new[]
        {
            screen is { Length: > 0 } and not "off" ? $"frame {screen}" : "",
            depth.Length > 0 ? $"in the {depth}" : "",
            mark.Trim().TrimEnd('.'),
        }.Where(p => p.Length > 0));

    private static string Moving(string moving)
    {
        if (moving.Length == 0)
        {
            return "";
        }

        if (moving == "still")
        {
            return "not moving";
        }

        var parts = moving.Split(", ").Select(part => part switch
        {
            "toward camera" => "coming toward the camera",
            "away from camera" => "going away from the camera",
            ScreenDirections.LeftToRight => "moving from screen left toward screen right",
            ScreenDirections.RightToLeft => "moving from screen right toward screen left",
            _ => part,
        });
        return string.Join(", ", parts);
    }

    /// <summary>Ahead of the references: what they are for, and that the description wins.</summary>
    public const string ReferencesPreamble = """
        Pictures already made for this production follow, each after a line saying what it is. Use them
        for continuity: keep faces, bodies, clothes, places, objects and light as they are pictured,
        wherever that agrees with the picture to make, described at the end. Where a reference disagrees
        with the description, or is marked out of date, the description wins. Do not copy a reference's
        composition or camera unless the description asks for the same shot.
        """;

    public static ImageRequest Request(string size, string prompt, IReadOnlyList<ImageInput> references) =>
        references.Count == 0
            ? new ImageRequest(size, "", [], prompt)
            : new ImageRequest(size, ReferencesPreamble, references, $"The picture to make:\n{prompt}");

    public static string Caption(int number, ReferenceCandidate candidate) =>
        $"Image {number}: {candidate.Caption}" +
        (candidate.Stale ? " It is out of date: what it pictures has changed since, so where it differs, follow the description." : "");

    /// <summary>"Refrigerated cooler (sealed): a white hard-shell box." for each, with the scene's state where known.</summary>
    private static string Carried(IReadOnlyList<PropRecord> holding, IReadOnlyList<FramePropView>? inView = null) =>
        string.Join(" ", holding.Select(prop =>
        {
            var state = inView?.FirstOrDefault(p => p.Prop.Id == prop.Id)?.State ?? [];
            var stateText = state.Count > 0 ? $" ({string.Join(", ", state)})" : "";
            return $"{prop.Name}{stateText}{(string.IsNullOrWhiteSpace(prop.Description) ? "" : $": {prop.Description.Trim().TrimEnd('.')}")}.";
        }));

    private static void AppendIdentity(StringBuilder text, CharacterRecord character)
    {
        if (character.Appearance is not { } identity)
        {
            return;
        }

        var parts = new[]
        {
            Part("Age", identity.Age),
            Part("Gender", identity.Gender ?? ""),
            Part("Build", identity.Build),
            Part("Face", identity.Face),
            Part("Hair", identity.Hair),
            Part("Skin", identity.Skin),
            identity.Marks.Count > 0 ? $"Always visible: {string.Join(", ", identity.Marks)}." : "",
        }.Where(p => p.Length > 0);
        text.AppendLine(string.Join(' ', parts));
    }

    /// <param name="plan">A frame chooses its own camera, so the layout is offered as a map of the place.</param>
    private static void AppendPlace(StringBuilder text, LocationRecord location, bool plan = false)
    {
        if (location.Appearance is not { } appearance)
        {
            return;
        }

        AppendLine(text, plan && appearance.Layout.Trim().Length > 0
            ? $"How the place is laid out (a map of it, not this picture's camera): {appearance.Layout.Trim()}"
            : appearance.Layout);
        AppendLine(text, appearance.KeyFeatures);
    }

    private static void AppendLine(StringBuilder text, string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            text.AppendLine(line.Trim());
        }
    }

    private static string Part(string name, string value) =>
        string.IsNullOrWhiteSpace(value) ? "" : $"{name}: {value.Trim().TrimEnd('.')}.";

    private static string Wardrobe(string label, string description) =>
        string.IsNullOrWhiteSpace(description) ? label : $"{label}: {description.Trim().TrimEnd('.')}";

    private static string Setting(LocationSettingRecord setting) =>
        setting == LocationSettingRecord.Interior ? "an interior" : "an exterior";

    private static string People(int count) => count switch
    {
        1 => "one person",
        2 => "two people",
        3 => "three people",
        _ => $"{count} people",
    };
}
