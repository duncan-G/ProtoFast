using ProtoFast.Api.Services.Directing;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using CharacterWardrobeRecord = ProtoFast.Data.ThePlot.Entities.CharacterWardrobe;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>A character as one frame shows them, with the scene's and the frame's changes already added up.</summary>
/// <param name="Look">The look in force at the frame, before what the frame alone adds; its picture is their reference.</param>
/// <param name="Holding">What they carry in the frame.</param>
public sealed record FrameCharacter(
    CharacterRecord Character,
    CharacterWardrobeRecord? Wardrobe,
    string WardrobeChange,
    string AppearanceChange,
    string Screen,
    string Pose,
    string Expression,
    LookInForce Look,
    IReadOnlyList<PropRecord> Holding);
