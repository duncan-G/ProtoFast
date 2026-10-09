using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using CharacterWardrobeRecord = ProtoFast.Data.ThePlot.Entities.CharacterWardrobe;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>A character as one frame shows them, with the scene's and the frame's changes already added up.</summary>
public sealed record FrameCharacter(
    CharacterRecord Character,
    CharacterWardrobeRecord? Wardrobe,
    string WardrobeChange,
    string AppearanceChange,
    string Screen,
    string Pose,
    string Expression);
