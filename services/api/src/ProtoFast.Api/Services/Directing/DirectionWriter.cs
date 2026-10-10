
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

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// Writes a draft over a scene's stored director layer. Rows are matched by their place in the
/// scene (a cue by its line, a frame by its first row, a look by its character, a look change by its
/// character and line, a state by its prop or location, an extra by its library entry), so they keep their ids; rows the draft leaves out are deleted and new ones get ids from
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
            // A pass that read the frame before the author locked it leaves it alone.
            if (stored.Locked && !next.Locked && !byAuthor)
            {
                return false;
            }

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
                          || stored.Camera != next.Camera
                          || stored.Variation != next.Variation
                          || stored.HoldMs != next.HoldMs
                          || !SamePlacements(stored.Placements, next.Placements)
                          || !SameProps(stored.Props, next.Props)
                          || !SameExtras(stored.Extras, next.Extras);
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
            stored.Camera = next.Camera;
            stored.Variation = next.Variation;
            stored.HoldMs = next.HoldMs;
            // Locking says the author is done, not that they wrote the frame; only the author does it.
            if (byAuthor)
            {
                stored.Locked = next.Locked;
            }

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
                    existing.Facing = placement.Facing;
                    existing.Moving = placement.Moving;
                    existing.Looking = placement.Looking;
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

                stored.Extras.RemoveAll(e => next.Extras.All(n => n.ExtraId != e.ExtraId));
                foreach (var extra in next.Extras)
                {
                    var existing = stored.Extras.FirstOrDefault(e => e.ExtraId == extra.ExtraId);
                    if (existing is null)
                    {
                        stored.Extras.Add(extra);
                    }
                    else
                    {
                        existing.OnScreen = extra.OnScreen;
                        existing.Screen = extra.Screen;
                        existing.Facing = extra.Facing;
                        existing.Moving = extra.Moving;
                        existing.Looking = extra.Looking;
                        existing.Pose = extra.Pose;
                    }
                }
            }

            // A locked frame keeps its edited mark through a pass that only pins the scene's framing on it.
            return changed && !next.Locked;
        });

        Sync(scene.Appearances, draft.Appearances, a => a.CharacterId, byAuthor, (stored, next) =>
        {
            var changed = stored.CharacterId != next.CharacterId
                          || stored.OnScreen != next.OnScreen
                          || stored.WardrobeId != next.WardrobeId
                          || next.Wardrobe is not null
                          || stored.WardrobeChange != next.WardrobeChange
                          || stored.AppearanceChange != next.AppearanceChange
                          || !stored.Holding.SequenceEqual(next.Holding)
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
            // The frames pass sets it, so it is no evidence the author wrote the look.
            stored.Travel = next.Travel;
            stored.WardrobeChange = next.WardrobeChange;
            stored.AppearanceChange = next.AppearanceChange;
            stored.Holding = next.Holding;
            stored.ChangeReason = next.ChangeReason;
            stored.ChangeElementId = next.ChangeElementId;
            return changed;
        });

        Sync(scene.LookChanges, draft.LookChanges, c => (c.CharacterId, c.ElementId), byAuthor, (stored, next) =>
        {
            var changed = stored.WardrobeId != next.WardrobeId
                          || next.Wardrobe is not null
                          || stored.WardrobeChange != next.WardrobeChange
                          || stored.AppearanceChange != next.AppearanceChange
                          || !stored.Holding.SequenceEqual(next.Holding)
                          || stored.Reason != next.Reason;
            if (next.Wardrobe is not null)
            {
                stored.Wardrobe = next.Wardrobe;
            }
            else
            {
                stored.WardrobeId = next.WardrobeId;
                stored.Wardrobe = null;
            }

            stored.WardrobeChange = next.WardrobeChange;
            stored.AppearanceChange = next.AppearanceChange;
            stored.Holding = next.Holding;
            stored.Reason = next.Reason;
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

        Sync(scene.Extras, draft.Extras, e => e.ExtraId, byAuthor, (stored, next) =>
        {
            var changed = stored.OnScreen != next.OnScreen;
            stored.OnScreen = next.OnScreen;
            return changed;
        });
    }

    /// <param name="key">The row's place in the scene.</param>
    /// <param name="overwrite">Copies the draft row onto the stored one and says whether anything changed.</param>
    private static void Sync<T, TKey>(List<T> stored, List<T> next, Func<T, TKey> key, bool byAuthor, Func<T, T, bool> overwrite)
        where T : class
        where TKey : notnull
    {
        var keep = next.Select(key).ToHashSet();
        stored.RemoveAll(row => !keep.Contains(key(row)));
        foreach (var row in next)
        {
            var existing = stored.FirstOrDefault(s => EqualityComparer<TKey>.Default.Equals(key(s), key(row)));
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
            case SceneLookChangeRecord change:
                change.Id = id;
                break;
            case ScenePropStateRecord state:
                state.Id = id;
                break;
            case SceneLocationStateRecord location:
                location.Id = id;
                break;
            case SceneExtraRecord extra:
                extra.Id = id;
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
            case SceneLookChangeRecord change:
                change.Edited = edited;
                break;
            case ScenePropStateRecord state:
                state.Edited = edited;
                break;
            case SceneLocationStateRecord location:
                location.Edited = edited;
                break;
            case SceneExtraRecord extra:
                extra.Edited = edited;
                break;
        }
    }

    private static bool SamePlacements(List<FramePlacementRecord> stored, List<FramePlacementRecord> next) =>
        stored.Count == next.Count
        && stored.All(s => next.Any(n =>
            n.CharacterId == s.CharacterId
            && n.Screen == s.Screen
            && n.Facing == s.Facing
            && n.Moving == s.Moving
            && n.Looking == s.Looking
            && n.Pose == s.Pose
            && n.Expression == s.Expression
            && n.OnScreen == s.OnScreen
            && n.WardrobeId == s.WardrobeId
            && n.WardrobeChange == s.WardrobeChange
            && n.AppearanceChange == s.AppearanceChange));

    private static bool SameExtras(List<FrameExtraRecord> stored, List<FrameExtraRecord> next) =>
        stored.Count == next.Count
        && stored.All(s => next.Any(n =>
            n.ExtraId == s.ExtraId
            && n.OnScreen == s.OnScreen
            && n.Screen == s.Screen
            && n.Facing == s.Facing
            && n.Moving == s.Moving
            && n.Looking == s.Looking
            && n.Pose == s.Pose));

    private static bool SameProps(List<FramePropRecord> stored, List<FramePropRecord> next) =>
        stored.Count == next.Count && stored.All(s => next.Any(n => n.PropId == s.PropId && n.InView == s.InView));
}
