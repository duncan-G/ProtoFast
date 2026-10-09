
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;
using FramePlacementRecord = ProtoFast.Data.ThePlot.Entities.FramePlacement;
using FramePropRecord = ProtoFast.Data.ThePlot.Entities.FrameProp;
using CueRecord = ProtoFast.Data.ThePlot.Entities.Cue;
using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
using ScenePropStateRecord = ProtoFast.Data.ThePlot.Entities.ScenePropState;
using SceneLocationStateRecord = ProtoFast.Data.ThePlot.Entities.SceneLocationState;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// Writes a draft over a scene's stored director layer. Rows are matched by their place in the
/// scene (a cue by its line, a frame by its first row, a look by its character, a state by its
/// prop or location), so they keep their ids; rows the draft leaves out are deleted and new ones get ids from
/// the database. A row the author changed is marked edited, so a later directing pass keeps it; a
/// row a directing pass changed loses the mark.
/// </summary>
public static class DirectionWriter
{
    public static void Apply(SceneRecord scene, DirectionDraft draft, bool byAuthor)
    {
        scene.StoryDay = draft.StoryDay;
        scene.Shot = draft.Shot;
        scene.Angle = draft.Angle;
        scene.Movement = draft.Movement;

        Sync(scene.Cues, draft.Cues, c => c.ElementId, byAuthor, (stored, next) =>
        {
            var changed = stored.ElementId != next.ElementId
                          || stored.Performance != next.Performance
                          || stored.GapBeforeMs != next.GapBeforeMs
                          || stored.Interrupted != next.Interrupted
                          || stored.NarratorId != next.NarratorId;
            stored.ElementId = next.ElementId;
            stored.Performance = next.Performance;
            stored.GapBeforeMs = next.GapBeforeMs;
            stored.Interrupted = next.Interrupted;
            stored.NarratorId = next.NarratorId;
            return changed;
        });

        Sync(scene.Frames, draft.Frames, p => p.FirstElementId, byAuthor, (stored, next) =>
        {
            // Position is where the frame sits among the others, not something the author wrote on it.
            var changed = stored.FirstElementId != next.FirstElementId
                          || stored.LastElementId != next.LastElementId
                          || stored.LocationId != next.LocationId
                          || stored.TimeOfDay != next.TimeOfDay
                          || stored.Beat != next.Beat
                          || stored.Shot != next.Shot
                          || stored.Angle != next.Angle
                          || stored.Movement != next.Movement
                          || stored.LocationChange != next.LocationChange
                          || stored.HoldMs != next.HoldMs
                          || !SamePlacements(stored.Placements, next.Placements)
                          || !SameProps(stored.Props, next.Props);
            stored.Position = next.Position;
            stored.FirstElementId = next.FirstElementId;
            stored.LastElementId = next.LastElementId;
            stored.LocationId = next.LocationId;
            stored.TimeOfDay = next.TimeOfDay;
            stored.Beat = next.Beat;
            stored.Shot = next.Shot;
            stored.Angle = next.Angle;
            stored.Movement = next.Movement;
            stored.LocationChange = next.LocationChange;
            stored.HoldMs = next.HoldMs;
            if (changed)
            {
                stored.Placements.RemoveAll(p => next.Placements.All(n => n.CharacterId != p.CharacterId));
                foreach (var placement in next.Placements)
                {
                    var existing = stored.Placements.FirstOrDefault(p => p.CharacterId == placement.CharacterId);
                    if (existing is null)
                    {
                        stored.Placements.Add(placement);
                        continue;
                    }

                    existing.Screen = placement.Screen;
                    existing.Pose = placement.Pose;
                    existing.Expression = placement.Expression;
                    existing.OnScreen = placement.OnScreen;
                    existing.WardrobeId = placement.WardrobeId;
                    existing.WardrobeChange = placement.WardrobeChange;
                    existing.AppearanceChange = placement.AppearanceChange;
                }

                stored.Props.RemoveAll(p => next.Props.All(n => n.PropId != p.PropId));
                foreach (var prop in next.Props)
                {
                    var existing = stored.Props.FirstOrDefault(p => p.PropId == prop.PropId);
                    if (existing is null)
                    {
                        stored.Props.Add(prop);
                    }
                    else
                    {
                        existing.InView = prop.InView;
                    }
                }
            }

            return changed;
        });

        Sync(scene.Appearances, draft.Appearances, a => a.CharacterId, byAuthor, (stored, next) =>
        {
            var changed = stored.CharacterId != next.CharacterId
                          || stored.OnScreen != next.OnScreen
                          || stored.WardrobeId != next.WardrobeId
                          || next.Wardrobe is not null
                          || stored.WardrobeChange != next.WardrobeChange
                          || stored.AppearanceChange != next.AppearanceChange
                          || stored.ChangeReason != next.ChangeReason
                          || stored.ChangeElementId != next.ChangeElementId;
            stored.CharacterId = next.CharacterId;
            if (next.Wardrobe is not null)
            {
                // A wardrobe the directing pass added to the sheet: it gets its id on save.
                stored.Wardrobe = next.Wardrobe;
            }
            else
            {
                stored.WardrobeId = next.WardrobeId;
                stored.Wardrobe = null;
            }

            stored.OnScreen = next.OnScreen;
            stored.WardrobeChange = next.WardrobeChange;
            stored.AppearanceChange = next.AppearanceChange;
            stored.ChangeReason = next.ChangeReason;
            stored.ChangeElementId = next.ChangeElementId;
            return changed;
        });

        Sync(scene.PropStates, draft.PropStates, p => p.PropId, byAuthor, (stored, next) =>
        {
            var changed = stored.PropId != next.PropId
                          || stored.InView != next.InView
                          || !stored.State.SequenceEqual(next.State)
                          || stored.ChangeReason != next.ChangeReason
                          || stored.ChangeElementId != next.ChangeElementId;
            stored.PropId = next.PropId;
            stored.InView = next.InView;
            stored.State = next.State;
            stored.ChangeReason = next.ChangeReason;
            stored.ChangeElementId = next.ChangeElementId;
            return changed;
        });

        Sync(scene.LocationStates, draft.LocationStates, l => l.LocationId, byAuthor, (stored, next) =>
        {
            var changed = stored.Change != next.Change;
            stored.Change = next.Change;
            return changed;
        });
    }

    /// <param name="key">The row's place in the scene.</param>
    /// <param name="overwrite">Copies the draft row onto the stored one and says whether anything changed.</param>
    private static void Sync<T>(List<T> stored, List<T> next, Func<T, Guid> key, bool byAuthor, Func<T, T, bool> overwrite)
        where T : class
    {
        var keep = next.Select(key).ToHashSet();
        stored.RemoveAll(row => !keep.Contains(key(row)));
        foreach (var row in next)
        {
            var existing = stored.FirstOrDefault(s => key(s) == key(row));
            if (existing is null)
            {
                // A fresh row with no id: the database assigns one. An id the draft carried over
                // from a row that no longer exists would be taken for an existing row.
                SetId(row, Guid.Empty);
                SetEdited(row, byAuthor);
                stored.Add(row);
            }
            else if (overwrite(existing, row))
            {
                SetEdited(existing, byAuthor);
            }
        }
    }

    private static void SetId<T>(T row, Guid id)
    {
        switch (row)
        {
            case CueRecord cue:
                cue.Id = id;
                break;
            case FrameRecord frame:
                frame.Id = id;
                break;
            case SceneAppearanceRecord appearance:
                appearance.Id = id;
                break;
            case ScenePropStateRecord state:
                state.Id = id;
                break;
            case SceneLocationStateRecord location:
                location.Id = id;
                break;
        }
    }

    private static void SetEdited<T>(T row, bool edited)
    {
        switch (row)
        {
            case CueRecord cue:
                cue.Edited = edited;
                break;
            case FrameRecord frame:
                frame.Edited = edited;
                break;
            case SceneAppearanceRecord appearance:
                appearance.Edited = edited;
                break;
            case ScenePropStateRecord state:
                state.Edited = edited;
                break;
            case SceneLocationStateRecord location:
                location.Edited = edited;
                break;
        }
    }

    private static bool SamePlacements(List<FramePlacementRecord> stored, List<FramePlacementRecord> next) =>
        stored.Count == next.Count
        && stored.All(s => next.Any(n =>
            n.CharacterId == s.CharacterId
            && n.Screen == s.Screen
            && n.Pose == s.Pose
            && n.Expression == s.Expression
            && n.OnScreen == s.OnScreen
            && n.WardrobeId == s.WardrobeId
            && n.WardrobeChange == s.WardrobeChange
            && n.AppearanceChange == s.AppearanceChange));

    private static bool SameProps(List<FramePropRecord> stored, List<FramePropRecord> next) =>
        stored.Count == next.Count && stored.All(s => next.Any(n => n.PropId == s.PropId && n.InView == s.InView));
}
