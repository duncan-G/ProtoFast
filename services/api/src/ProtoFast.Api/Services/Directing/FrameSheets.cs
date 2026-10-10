using FrameExtraRecord = ProtoFast.Data.ThePlot.Entities.FrameExtra;
using FramePlacementRecord = ProtoFast.Data.ThePlot.Entities.FramePlacement;
using FramePropRecord = ProtoFast.Data.ThePlot.Entities.FrameProp;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;

namespace ProtoFast.Api.Services.Directing;

/// <summary>Frames as the director's prompts and checks read them.</summary>
internal static class FrameSheets
{
    public static string CharacterName(SceneBrief brief, Guid id) =>
        brief.Context.Story.Characters.FirstOrDefault(c => c.Id == id)?.Name ?? "";

    public static string PropName(SceneBrief brief, Guid id) =>
        brief.Context.Story.Props.FirstOrDefault(p => p.Id == id)?.Name ?? "";

    public static string ExtraName(SceneBrief brief, Guid id) =>
        brief.Context.Story.Extras.FirstOrDefault(e => e.Id == id)?.Name ?? "";

    /// <summary>"Mara is screen left, in the foreground, at the counter, facing screen right, leaning, tense."</summary>
    public static string Placed(SceneBrief brief, FramePlacementRecord placement)
    {
        var parts = new[]
            {
                placement.Screen.Length > 0 ? $"is screen {placement.Screen}" : "is in the picture",
                placement.Depth.Length > 0 ? $"in the {placement.Depth}" : "",
                placement.Mark,
                placement.Facing.Length > 0 ? $"facing {placement.Facing}" : "",
                placement.Moving.Length > 0 ? $"moving {placement.Moving}" : "",
                placement.Looking.Length > 0 ? $"looking at {placement.Looking}" : "",
                placement.Pose,
                placement.Expression,
            }
            .Where(p => p.Length > 0);
        return $"{CharacterName(brief, placement.CharacterId)} {string.Join(", ", parts)}.";
    }

    /// <summary>"left, foreground, at the counter, facing screen right, moving left to right, looking at the nurse"</summary>
    public static string Directions(FramePlacementRecord placement) =>
        Directions(placement.Screen, placement.Depth, placement.Mark, placement.Facing, placement.Moving, placement.Looking);

    public static string Directions(FrameExtraRecord extra) =>
        Directions(extra.Screen, extra.Depth, extra.Mark, extra.Facing, extra.Moving, extra.Looking);

    /// <summary>"right, background, on the counter beside the phone"</summary>
    public static string Where(FramePropRecord prop) =>
        string.Join(", ", new[] { prop.Screen, prop.Depth, prop.Mark }.Where(s => s.Length > 0));

    private static string Directions(string screen, string depth, string mark, string facing, string moving, string looking) =>
        string.Join(", ", new[]
        {
            screen,
            depth,
            mark,
            facing.Length > 0 ? $"facing {facing}" : "",
            moving.Length > 0 ? $"moving {moving}" : "",
            looking.Length > 0 ? $"looking at {looking}" : "",
        }.Where(s => s.Length > 0));

    /// <summary>A frame as a rewrite reads it: rows, shot, camera, who and what is where, what it says now.</summary>
    public static string Staging(SceneBrief brief, DirectionDraft draft, FrameRecord frame)
    {
        var range = Range(brief, frame);
        var rows = range is null ? "?" : $"{brief.Label(brief.Elements[range.Value.First].Id)}–{brief.Label(brief.Elements[range.Value.Last].Id)}";
        var cast = OnScreen(draft, frame).Select(p =>
            $"{CharacterName(brief, p.CharacterId)} ({string.Join(", ", new[] { Directions(p), p.Pose, p.Expression }.Where(s => s.Length > 0))})").ToList();
        var extras = frame.Extras.Where(e => e.OnScreen != false && Directions(e).Length + e.Pose.Length > 0).Select(e =>
            $"{ExtraName(brief, e.ExtraId)} ({string.Join(", ", new[] { Directions(e), e.Pose }.Where(s => s.Length > 0))})").ToList();
        var props = frame.Props.Where(p => p.InView).Select(p =>
            Where(p) is { Length: > 0 } where ? $"{PropName(brief, p.PropId)} ({where})" : PropName(brief, p.PropId)).Where(n => n.Length > 0).ToList();
        var camera = frame.Camera.Length > 0 ? $", camera {frame.Camera}" : "";
        return $"rows {rows}, {frame.Shot ?? "scene"} shot{camera}; on screen: {(cast.Count == 0 ? "nobody" : string.Join(", ", cast))}; "
               + (extras.Count == 0 ? "" : $"extras staged: {string.Join(", ", extras)}; ")
               + $"props in view: {(props.Count == 0 ? "none" : string.Join(", ", props))}; has now: \"{frame.Beat}\"";
    }

    public static (int First, int Last)? Range(SceneBrief brief, FrameRecord frame)
    {
        var first = brief.Elements.FindIndex(e => e.Id == frame.FirstElementId);
        var last = brief.Elements.FindIndex(e => e.Id == frame.LastElementId);
        return first < 0 || last < 0 ? null : (Math.Min(first, last), Math.Max(first, last));
    }

    /// <summary>Placements of people on screen; a directed frame says so itself, a stored one may follow the scene.</summary>
    public static IEnumerable<FramePlacementRecord> OnScreen(DirectionDraft draft, FrameRecord frame) =>
        frame.Placements.Where(p =>
            p.OnScreen ?? draft.Appearances.FirstOrDefault(a => a.CharacterId == p.CharacterId)?.OnScreen ?? true);
}
