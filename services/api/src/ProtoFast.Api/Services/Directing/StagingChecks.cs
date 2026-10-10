using static ProtoFast.Api.Services.Directing.FrameSheets;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;

namespace ProtoFast.Api.Services.Directing;

/// <summary>What code can find wrong with where a frame puts people and props, before any model looks.</summary>
internal static class StagingChecks
{
    /// <summary>
    /// What breaks screen direction in <paramref name="frame"/>: moving against a travel, or two
    /// people on opposite sides of each other from the frame before (and the frame after, when that
    /// one is not being directed too, since it will not be checked itself).
    /// </summary>
    public static List<string> DirectionIssues(
        SceneBrief brief, DirectionDraft draft, List<FrameRecord> ordered, IReadOnlyCollection<FrameRecord> directed, FrameRecord frame)
    {
        var issues = new List<string>();
        foreach (var placement in OnScreen(draft, frame))
        {
            var travel = draft.Appearances.FirstOrDefault(a => a.CharacterId == placement.CharacterId)?.Travel ?? "";
            if (ScreenDirections.Horizontal(placement.Moving) is { } going && travel.Length > 0 && going != travel)
            {
                issues.Add($"{CharacterName(brief, placement.CharacterId)} moves {going} but travels {travel} in this scene");
            }

            if (ScreenDirections.Contradiction(placement.Facing, placement.Moving) is { } contradiction)
            {
                issues.Add($"{CharacterName(brief, placement.CharacterId)} {contradiction}");
            }
        }

        var at = ordered.IndexOf(frame);
        if (at > 0)
        {
            issues.AddRange(Swaps(brief, draft, ordered[at - 1], frame, "the frame before"));
        }

        if (at >= 0 && at + 1 < ordered.Count && !directed.Contains(ordered[at + 1]))
        {
            issues.AddRange(Swaps(brief, draft, frame, ordered[at + 1], "the frame after"));
        }

        return issues;
    }

    /// <summary>
    /// Someone or something in the picture with no place in it: a character without a side, depth
    /// or mark, a staged extra or a loose prop without a mark, or two people on one spot. Carried
    /// props go with whoever carries them, so they need none.
    /// </summary>
    public static List<string> Gaps(SceneBrief brief, DirectionDraft draft, FrameRecord frame)
    {
        var issues = new List<string>();
        var onScreen = OnScreen(draft, frame).ToList();
        foreach (var placement in onScreen)
        {
            var name = CharacterName(brief, placement.CharacterId);
            var missing = new[]
            {
                placement.Screen.Length == 0 ? "screen side" : "",
                placement.Depth.Length == 0 ? "depth" : "",
                placement.Mark.Trim().Length == 0 ? "mark" : "",
            }.Where(m => m.Length > 0).ToList();
            if (missing.Count > 0)
            {
                issues.Add($"{name} is on screen with no {string.Join(" or ", missing)}: say where in the place and the picture they are");
            }
        }

        for (var i = 0; i < onScreen.Count; i++)
        {
            for (var j = i + 1; j < onScreen.Count; j++)
            {
                var (a, b) = (onScreen[i], onScreen[j]);
                if (a.Mark.Trim().Length > 0 && a.Screen == b.Screen && a.Depth == b.Depth && Same(a.Mark, b.Mark))
                {
                    issues.Add($"{CharacterName(brief, a.CharacterId)} and {CharacterName(brief, b.CharacterId)} are on the same spot "
                               + $"({a.Mark.Trim()}, {a.Screen}, {a.Depth}): put each where they stand");
                }
            }
        }

        var last = brief.Elements.FindIndex(e => e.Id == frame.LastElementId);
        var held = brief.Cast
            .SelectMany(c => Looks.At(draft.Appearances, draft.LookChanges, brief.Position, c.Id, last).Look.Holding)
            .ToHashSet();
        foreach (var prop in frame.Props.Where(p => p.InView && !held.Contains(p.PropId) && p.Mark.Trim().Length == 0))
        {
            issues.Add($"{PropName(brief, prop.PropId)} is in view with no mark: say where in the place it is");
        }

        foreach (var extra in frame.Extras.Where(e => e.OnScreen != false && e.Screen.Length + e.Facing.Length + e.Pose.Length > 0 && e.Mark.Trim().Length == 0))
        {
            issues.Add($"{ExtraName(brief, extra.ExtraId)} is staged with no mark: say where in the place they are");
        }

        return issues;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('.'), b.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Swaps(SceneBrief brief, DirectionDraft draft, FrameRecord before, FrameRecord after, string which)
    {
        var sidesBefore = Sides(draft, before);
        var sidesAfter = Sides(draft, after);
        var both = sidesBefore.Keys.Where(sidesAfter.ContainsKey).OrderBy(id => id).ToList();
        for (var i = 0; i < both.Count; i++)
        {
            for (var j = i + 1; j < both.Count; j++)
            {
                var (a, b) = (both[i], both[j]);
                var was = Math.Sign(sidesBefore[a] - sidesBefore[b]);
                var now = Math.Sign(sidesAfter[a] - sidesAfter[b]);
                if (was != 0 && now == -was)
                {
                    var (left, right) = was < 0 ? (a, b) : (b, a);
                    yield return $"{CharacterName(brief, left)} and {CharacterName(brief, right)} swap sides from {which}, " +
                                 $"where {CharacterName(brief, left)} is left of {CharacterName(brief, right)}";
                }
            }
        }
    }

    private static Dictionary<Guid, int> Sides(DirectionDraft draft, FrameRecord frame) =>
        OnScreen(draft, frame)
            .Select(p => (p.CharacterId, Order: ScreenDirections.Order(p.Screen)))
            .Where(p => p.Order is not null)
            .ToDictionary(p => p.CharacterId, p => p.Order!.Value);
}
