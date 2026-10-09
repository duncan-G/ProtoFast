using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;

namespace ProtoFast.Api.Services.Imaging;

/// <param name="HeldBy">The character on screen carrying it, if any.</param>
public sealed record FramePropView(PropRecord Prop, IReadOnlyList<string> State, CharacterRecord? HeldBy = null);
