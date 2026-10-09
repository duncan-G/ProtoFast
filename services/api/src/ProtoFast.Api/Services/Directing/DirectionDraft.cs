
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using PanelRecord = ProtoFast.Data.ThePlot.Entities.Panel;
using PanelPlacementRecord = ProtoFast.Data.ThePlot.Entities.PanelPlacement;
using PanelPropRecord = ProtoFast.Data.ThePlot.Entities.PanelProp;
using CueRecord = ProtoFast.Data.ThePlot.Entities.Cue;
using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
using ScenePropStateRecord = ProtoFast.Data.ThePlot.Entities.ScenePropState;
using SceneLocationStateRecord = ProtoFast.Data.ThePlot.Entities.SceneLocationState;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// A scene's director layer as detached rows, validated and ready to be written over the stored
/// ones. Panels are in scene order.
/// </summary>
public sealed class DirectionDraft
{
    public int? StoryDay { get; set; }

    public string? Shot { get; set; }

    public string? Angle { get; set; }

    public string? Movement { get; set; }

    public List<PanelRecord> Panels { get; set; } = [];

    public List<CueRecord> Cues { get; set; } = [];

    public List<SceneAppearanceRecord> Appearances { get; set; } = [];

    public List<ScenePropStateRecord> PropStates { get; set; } = [];

    public List<SceneLocationStateRecord> LocationStates { get; set; } = [];

    /// <summary>A copy of the scene's stored layer, for a directing pass to change one part of.</summary>
    public static DirectionDraft From(SceneRecord scene) => new()
    {
        StoryDay = scene.StoryDay,
        Shot = scene.Shot,
        Angle = scene.Angle,
        Movement = scene.Movement,
        Panels = scene.Panels.OrderBy(p => p.Position).ThenBy(p => p.Id).Select(p => new PanelRecord
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
            Placements = p.Placements.Select(pl => new PanelPlacementRecord
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
            Props = p.Props.Select(pp => new PanelPropRecord { PropId = pp.PropId, InView = pp.InView }).ToList(),
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
            ChangeReason = a.ChangeReason,
            ChangeElementId = a.ChangeElementId,
            Edited = a.Edited,
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
    };
}
