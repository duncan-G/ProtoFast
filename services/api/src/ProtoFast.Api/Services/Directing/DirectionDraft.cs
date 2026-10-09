
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;
using FramePlacementRecord = ProtoFast.Data.ThePlot.Entities.FramePlacement;
using FramePropRecord = ProtoFast.Data.ThePlot.Entities.FrameProp;
using CueRecord = ProtoFast.Data.ThePlot.Entities.Cue;
using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
using SceneLookChangeRecord = ProtoFast.Data.ThePlot.Entities.SceneLookChange;
using ScenePropStateRecord = ProtoFast.Data.ThePlot.Entities.ScenePropState;
using SceneLocationStateRecord = ProtoFast.Data.ThePlot.Entities.SceneLocationState;
using SceneExtraRecord = ProtoFast.Data.ThePlot.Entities.SceneExtra;
using FrameExtraRecord = ProtoFast.Data.ThePlot.Entities.FrameExtra;
using ExtraRecord = ProtoFast.Data.ThePlot.Entities.Extra;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// A scene's director layer as detached rows, validated and ready to be written over the stored
/// ones. Frames are in scene order.
/// </summary>
public sealed class DirectionDraft
{
    public int? StoryDay { get; set; }

    public string? Shot { get; set; }

    public string? Angle { get; set; }

    public string? Movement { get; set; }

    public List<FrameRecord> Frames { get; set; } = [];

    public List<CueRecord> Cues { get; set; } = [];

    public List<SceneAppearanceRecord> Appearances { get; set; } = [];

    public List<SceneLookChangeRecord> LookChanges { get; set; } = [];

    public List<ScenePropStateRecord> PropStates { get; set; } = [];

    public List<SceneLocationStateRecord> LocationStates { get; set; } = [];

    public List<SceneExtraRecord> Extras { get; set; } = [];

    /// <summary>Extras a directing pass found the library lacks; rows point at them by their new ids.</summary>
    public List<ExtraRecord> NewExtras { get; set; } = [];

    /// <summary>A copy of the scene's stored layer, for a directing pass to change one part of.</summary>
    public static DirectionDraft From(SceneRecord scene) => new()
    {
        StoryDay = scene.StoryDay,
        Shot = scene.Shot,
        Angle = scene.Angle,
        Movement = scene.Movement,
        Frames = scene.Frames.OrderBy(p => p.Position).ThenBy(p => p.Id).Select(p => new FrameRecord
        {
            Id = p.Id,
            FirstElementId = p.FirstElementId,
            LastElementId = p.LastElementId,
            LocationId = p.LocationId,
            TimeOfDay = p.TimeOfDay,
            Beat = p.Beat,
            Shot = p.Shot,
            Angle = p.Angle,
            Movement = p.Movement,
            LocationChange = p.LocationChange,
            HoldMs = p.HoldMs,
            Edited = p.Edited,
            Placements = p.Placements.Select(pl => new FramePlacementRecord
            {
                CharacterId = pl.CharacterId,
                Screen = pl.Screen,
                Pose = pl.Pose,
                Expression = pl.Expression,
                OnScreen = pl.OnScreen,
                WardrobeId = pl.WardrobeId,
                WardrobeChange = pl.WardrobeChange,
                AppearanceChange = pl.AppearanceChange,
            }).ToList(),
            Props = p.Props.Select(pp => new FramePropRecord { PropId = pp.PropId, InView = pp.InView }).ToList(),
            Extras = p.Extras.Select(e => new FrameExtraRecord { ExtraId = e.ExtraId, OnScreen = e.OnScreen }).ToList(),
        }).ToList(),
        Cues = scene.Cues.Select(c => new CueRecord
        {
            Id = c.Id,
            ElementId = c.ElementId,
            Performance = c.Performance,
            GapBeforeMs = c.GapBeforeMs,
            Interrupted = c.Interrupted,
            NarratorId = c.NarratorId,
            Edited = c.Edited,
        }).ToList(),
        Appearances = scene.Appearances.Select(a => new SceneAppearanceRecord
        {
            Id = a.Id,
            CharacterId = a.CharacterId,
            OnScreen = a.OnScreen,
            WardrobeId = a.WardrobeId,
            WardrobeChange = a.WardrobeChange,
            AppearanceChange = a.AppearanceChange,
            Holding = [.. a.Holding],
            ChangeReason = a.ChangeReason,
            ChangeElementId = a.ChangeElementId,
            Edited = a.Edited,
        }).ToList(),
        LookChanges = scene.LookChanges.Select(c => new SceneLookChangeRecord
        {
            Id = c.Id,
            CharacterId = c.CharacterId,
            ElementId = c.ElementId,
            WardrobeId = c.WardrobeId,
            WardrobeChange = c.WardrobeChange,
            AppearanceChange = c.AppearanceChange,
            Holding = [.. c.Holding],
            Reason = c.Reason,
            Edited = c.Edited,
        }).ToList(),
        PropStates = scene.PropStates.Select(p => new ScenePropStateRecord
        {
            Id = p.Id,
            PropId = p.PropId,
            InView = p.InView,
            State = [.. p.State],
            ChangeReason = p.ChangeReason,
            ChangeElementId = p.ChangeElementId,
            Edited = p.Edited,
        }).ToList(),
        LocationStates = scene.LocationStates.Select(l => new SceneLocationStateRecord
        {
            Id = l.Id,
            LocationId = l.LocationId,
            Change = l.Change,
            Edited = l.Edited,
        }).ToList(),
        Extras = scene.Extras.Select(e => new SceneExtraRecord
        {
            Id = e.Id,
            ExtraId = e.ExtraId,
            OnScreen = e.OnScreen,
            Edited = e.Edited,
        }).ToList(),
    };
}
