using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>A beat of prose or dialogue; headings and transitions are not beats.</summary>
public sealed record BeatInfo(Guid Id, SceneElementType Type, string Text, string? Speaker, string? Parenthetical);
