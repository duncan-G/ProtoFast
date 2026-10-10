using System.Text;
using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Screenplays;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
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
                text.AppendLine($"Carrying: {Carried(shown.Holding, frame.InView)}");
            }

            var staging = Staging(shown.Screen, shown.Facing, shown.Moving, shown.Looking, shown.Pose)
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
            var extra = shown.Extra;
            text.AppendLine();
            text.AppendLine(extra.Headcount is { } count
                ? $"{extra.Name} (extras, in the background, {Headcount.Words(count)}):"
                : $"{extra.Name} (extras, in the background):");
            AppendLine(text, extra.Description);
            if (!string.IsNullOrWhiteSpace(extra.Wardrobe))
            {
                text.AppendLine($"Wearing: {extra.Wardrobe.Trim()}");
            }

            var staging = Staging(shown.Screen, shown.Facing, shown.Moving, shown.Looking, shown.Pose).Where(p => p.Length > 0).ToList();
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
            }
        }

        text.AppendLine();
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

        if (frame.OnScreen.Count + frame.Extras.Count > 0)
        {
            text.AppendLine("Left and right mean the viewer's left and right in the picture, never a person's own.");
        }

        if (frame.Frame.Beat.Trim() is { Length: > 0 } beat)
        {
            text.AppendLine($"In the picture: {beat}");
        }

        if (frame.OffScreen.Count > 0)
        {
            text.AppendLine($"Not in the picture: {string.Join(", ", frame.OffScreen.Select(c => c.Name))}.");
        }

        text.AppendLine((frame.OnScreen.Count, frame.Extras.Count) switch
        {
            (0, 0) => "No people in the picture.",
            (0, _) => "No one in the picture but the extras described.",
            (var count, 0) => $"Exactly {People(count)} in the picture.",
            var (count, _) => $"Exactly {People(count)} in the picture besides the extras described.",
        });
        text.Append(NoText);
        return text.ToString();
    }

    /// <summary>"frame left", "turned three-quarters toward screen right", "coming toward the camera, …"</summary>
    private static IEnumerable<string> Staging(string screen, string facing, string moving, string looking, string pose)
    {
        yield return screen is { Length: > 0 } and not "off" ? $"frame {screen}" : "";
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
