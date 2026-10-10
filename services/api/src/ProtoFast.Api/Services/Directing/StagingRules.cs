using DirectionVocabulary = ProtoFast.Data.ThePlot.Entities.DirectionVocabulary;

namespace ProtoFast.Api.Services.Directing;

/// <summary>How frames stage people and props, as the director and the verifiers both read it.</summary>
internal static class StagingRules
{
    /// <summary>The picture model needs the camera and every direction spelled out, or it picks its own.</summary>
    public static readonly string Staging = $"""
        "camera" is where the camera stands in the place and what it looks toward, in the place's own
        terms ("inside the corridor a few steps past the double doors, looking back at them"). Put it
        where the action reads: someone coming through a door is seen from the side they come into,
        with the door behind them; two people talking are seen with both faces in view.
        For each character, and each extra you stage, "mark" is the spot in the place where they are,
        named by what is there and in the place's own terms ("at the near end of the nurse station
        counter", "in the doorway of trauma room 2", "two steps inside the double doors"); the place's
        layout and its pictures say what is where. "screen" is the side of the picture that mark falls
        on ({string.Join("|", DirectionVocabulary.ScreenSides)}) and "depth" how far into the picture
        it is ({string.Join("|", DirectionVocabulary.Depths)}), both from where the camera stands.
        "facing" is the way their body is turned ({string.Join("|", DirectionVocabulary.Facings)}),
        "moving" the way they are going across the picture ({string.Join("|", DirectionVocabulary.Movings)})
        and "looking" who or what they look at, by name. All of these as the viewer sees the picture.
        A three-quarter turn shows the face, a back three-quarter turn shows the back: someone going
        away from the camera, or through a door away from it, has their back to it. The body faces the
        way it moves; the head can turn to whoever they look at. Stage an extra when the rows single
        one of them out, and say in its "pose" which of the group it is. Every prop in view that
        nobody carries has a "mark", "screen" and "depth" too ("on the counter beside the phone"); a
        carried prop goes with whoever carries it and has none.
        Screen direction: someone who moves keeps the same "travel" ({string.Join("|", DirectionVocabulary.Travels)})
        in every frame of the scene, so a cut never reverses them, and two people keep the same sides
        of each other from frame to frame unless a frame shows one cross the other. Only turn someone
        around when the script does, and then show the turn in "visible".
        """;

    /// <summary>Where people and props are follows from the frame before, so the author need not move them by hand.</summary>
    public const string Continuity = """
        Positions carry from frame to frame. A mark belongs to the place, not to the picture, so it
        stays when the camera moves; "screen" and "depth" are where that mark falls from the new
        camera. Unless the rows in between move them, everyone is on the mark they had in the frame
        before and every prop nobody moved is where it was; while the camera stays put they keep their
        sides and depths too. Someone who moves goes from their last mark toward where the rows send
        them and is caught on the way. Someone who comes in arrives through a way into the place (a
        door, the corridor, the stairs); someone who leaves is gone from the frames after. Two people
        are never on one spot unless they touch. People who talk to each other are where they can see
        each other. The people the rows are about are where the eye goes first, with extras around
        them, never in front of them unless the shot is through them.
        """;
}
