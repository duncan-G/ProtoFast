using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Directing;

/// <summary>A scene with its elements and director layer, its story's library and outline, and the scenes before it.</summary>
/// <param name="Earlier">In story order, with their elements and director layers; empty unless asked for.</param>
public sealed record DirectedScene(
    SceneRecord Scene,
    StoryRecord Story,
    IReadOnlyList<SceneRecord> Earlier,
    DirectionReferences References);
