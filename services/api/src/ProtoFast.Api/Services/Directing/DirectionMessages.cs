using CueRecord = ProtoFast.Data.ThePlot.Entities.Cue;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;
using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;
using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
using SceneLookChangeRecord = ProtoFast.Data.ThePlot.Entities.SceneLookChange;
using SceneLocationStateRecord = ProtoFast.Data.ThePlot.Entities.SceneLocationState;
using ScenePropStateRecord = ProtoFast.Data.ThePlot.Entities.ScenePropState;
using SceneExtraRecord = ProtoFast.Data.ThePlot.Entities.SceneExtra;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;

namespace ProtoFast.Api.Services.Directing;

public static class DirectionMessages
{
    /// <param name="audio">Each cue's audio as the client plays it; null leaves the cues silent.</param>
    /// <param name="image">Each frame's picture as the client shows it; null leaves the frames unpictured.</param>
    /// <param name="look">A look's picture as the client shows it, from the character, the look and the row's asset; null leaves the looks unpictured.</param>
    public static SceneDirection ToMessage(
        SceneRecord scene,
        Func<CueRecord, CueAudio?>? audio = null,
        Func<FrameRecord, MediaImage?>? image = null,
        Func<Guid, Look, MediaAssetRecord?, MediaImage?>? look = null)
    {
        var message = new SceneDirection { SceneId = scene.Id.ToString() };
        if (scene.StoryDay is { } day)
        {
            message.StoryDay = day;
        }

        if (scene.Shot is not null)
        {
            message.Shot = scene.Shot;
        }

        if (scene.Angle is not null)
        {
            message.Angle = scene.Angle;
        }

        if (scene.Movement is not null)
        {
            message.Movement = scene.Movement;
        }

        message.Frames.AddRange(scene.Frames.OrderBy(p => p.Position).ThenBy(p => p.Id).Select(frame =>
        {
            var frameMessage = ToMessage(frame);
            if (image?.Invoke(frame) is { } pictured)
            {
                frameMessage.Image = pictured;
            }

            return frameMessage;
        }));
        var order = scene.Elements.ToDictionary(e => e.Id, e => e.Position);
        message.Cues.AddRange(scene.Cues
            .OrderBy(c => order.TryGetValue(c.ElementId, out var position) ? position : int.MaxValue)
            .ThenBy(c => c.Id)
            .Select(cue =>
            {
                var cueMessage = ToMessage(cue);
                if (audio?.Invoke(cue) is { } spoken)
                {
                    cueMessage.Audio = spoken;
                }

                return cueMessage;
            }));
        message.Appearances.AddRange(scene.Appearances.OrderBy(a => a.Id).Select(appearance =>
        {
            var appearanceMessage = ToMessage(appearance);
            if (look?.Invoke(appearance.CharacterId, Look.Of(appearance), appearance.Image) is { } pictured)
            {
                appearanceMessage.Image = pictured;
            }

            return appearanceMessage;
        }));
        message.LookChanges.AddRange(scene.LookChanges
            .OrderBy(c => c.CharacterId)
            .ThenBy(c => order.TryGetValue(c.ElementId, out var position) ? position : int.MaxValue)
            .Select(change =>
            {
                var changeMessage = ToMessage(change);
                if (look?.Invoke(change.CharacterId, Look.Of(change), change.Image) is { } pictured)
                {
                    changeMessage.Image = pictured;
                }

                return changeMessage;
            }));
        message.PropStates.AddRange(scene.PropStates.OrderBy(p => p.Id).Select(ToMessage));
        message.LocationStates.AddRange(scene.LocationStates.OrderBy(l => l.Id).Select(ToMessage));
        message.Extras.AddRange(scene.Extras.OrderBy(e => e.Id).Select(ToMessage));
        return message;
    }

    public static Frame ToMessage(FrameRecord frame)
    {
        var message = new Frame
        {
            Id = frame.Id.ToString(),
            Position = frame.Position,
            FirstElementId = frame.FirstElementId.ToString(),
            LastElementId = frame.LastElementId.ToString(),
            Beat = frame.Beat,
            LocationChange = frame.LocationChange,
            Camera = frame.Camera,
            Edited = frame.Edited,
        };
        if (frame.LocationId is { } locationId)
        {
            message.LocationId = locationId.ToString();
        }

        if (frame.TimeOfDay is not null)
        {
            message.TimeOfDay = frame.TimeOfDay;
        }

        if (frame.Shot is not null)
        {
            message.Shot = frame.Shot;
        }

        if (frame.Angle is not null)
        {
            message.Angle = frame.Angle;
        }

        if (frame.Movement is not null)
        {
            message.Movement = frame.Movement;
        }

        if (frame.HoldMs is { } holdMs)
        {
            message.HoldMs = holdMs;
        }

        message.Placements.AddRange(frame.Placements.OrderBy(p => p.CharacterId).Select(p =>
        {
            var placement = new Placement
            {
                CharacterId = p.CharacterId.ToString(),
                Screen = p.Screen,
                Facing = p.Facing,
                Moving = p.Moving,
                Looking = p.Looking,
                Pose = p.Pose,
                Expression = p.Expression,
                WardrobeChange = p.WardrobeChange,
                AppearanceChange = p.AppearanceChange,
            };
            if (p.OnScreen is { } onScreen)
            {
                placement.OnScreen = onScreen;
            }

            if (p.WardrobeId is { } wardrobeId)
            {
                placement.WardrobeId = wardrobeId.ToString();
            }

            return placement;
        }));
        message.Props.AddRange(frame.Props.OrderBy(p => p.PropId).Select(p => new FrameProp
        {
            PropId = p.PropId.ToString(),
            InView = p.InView,
        }));
        message.Extras.AddRange(frame.Extras.OrderBy(e => e.ExtraId).Select(e =>
        {
            var extra = new FrameExtra
            {
                ExtraId = e.ExtraId.ToString(),
                Screen = e.Screen,
                Facing = e.Facing,
                Moving = e.Moving,
                Looking = e.Looking,
                Pose = e.Pose,
            };
            if (e.OnScreen is { } onScreen)
            {
                extra.OnScreen = onScreen;
            }

            return extra;
        }));
        return message;
    }

    public static Cue ToMessage(CueRecord cue)
    {
        var message = new Cue
        {
            Id = cue.Id.ToString(),
            ElementId = cue.ElementId.ToString(),
            Performance = cue.Performance,
            GapBeforeMs = cue.GapBeforeMs,
            Interrupted = cue.Interrupted,
            Edited = cue.Edited,
        };
        if (cue.NarratorId is { } narratorId)
        {
            message.NarratorId = narratorId.ToString();
        }

        return message;
    }

    public static SceneAppearance ToMessage(SceneAppearanceRecord appearance)
    {
        var message = new SceneAppearance
        {
            Id = appearance.Id.ToString(),
            CharacterId = appearance.CharacterId.ToString(),
            OnScreen = appearance.OnScreen,
            Travel = appearance.Travel,
            WardrobeChange = appearance.WardrobeChange,
            AppearanceChange = appearance.AppearanceChange,
            Edited = appearance.Edited,
        };
        message.Holding.AddRange(appearance.Holding.Select(id => id.ToString()));
        if (appearance.WardrobeId is { } wardrobeId)
        {
            message.WardrobeId = wardrobeId.ToString();
        }

        if (appearance.ChangeReason is not null)
        {
            message.ChangeReason = appearance.ChangeReason;
        }

        if (appearance.ChangeElementId is { } elementId)
        {
            message.ChangeElementId = elementId.ToString();
        }

        return message;
    }

    public static LookChange ToMessage(SceneLookChangeRecord change)
    {
        var message = new LookChange
        {
            Id = change.Id.ToString(),
            CharacterId = change.CharacterId.ToString(),
            ElementId = change.ElementId.ToString(),
            WardrobeChange = change.WardrobeChange,
            AppearanceChange = change.AppearanceChange,
            Edited = change.Edited,
        };
        message.Holding.AddRange(change.Holding.Select(id => id.ToString()));
        if (change.WardrobeId is { } wardrobeId)
        {
            message.WardrobeId = wardrobeId.ToString();
        }

        if (change.Reason is not null)
        {
            message.Reason = change.Reason;
        }

        return message;
    }

    public static ScenePropState ToMessage(ScenePropStateRecord state)
    {
        var message = new ScenePropState
        {
            Id = state.Id.ToString(),
            PropId = state.PropId.ToString(),
            InView = state.InView,
            Edited = state.Edited,
        };
        message.State.AddRange(state.State);
        if (state.ChangeReason is not null)
        {
            message.ChangeReason = state.ChangeReason;
        }

        if (state.ChangeElementId is { } elementId)
        {
            message.ChangeElementId = elementId.ToString();
        }

        return message;
    }

    public static SceneExtra ToMessage(SceneExtraRecord extra) => new()
    {
        Id = extra.Id.ToString(),
        ExtraId = extra.ExtraId.ToString(),
        OnScreen = extra.OnScreen,
        Edited = extra.Edited,
    };

    public static SceneLocationState ToMessage(SceneLocationStateRecord state) => new()
    {
        Id = state.Id.ToString(),
        LocationId = state.LocationId.ToString(),
        Change = state.Change,
        Edited = state.Edited,
    };
}
