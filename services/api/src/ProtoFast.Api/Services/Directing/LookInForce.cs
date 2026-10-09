using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;
using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
using SceneLookChangeRecord = ProtoFast.Data.ThePlot.Entities.SceneLookChange;

namespace ProtoFast.Api.Services.Directing;

/// <summary>A character's look at one point of a scene, with the row that sets it.</summary>
/// <param name="Opening">The scene's look for them, if it has one.</param>
/// <param name="Change">The last change at or before the point, if any; it wins over the opening look.</param>
public sealed record LookInForce(Look Look, SceneAppearanceRecord? Opening, SceneLookChangeRecord? Change)
{
    public MediaAssetRecord? Image => Change is null ? Opening?.Image : Change.Image;

    /// <summary>Points the row that sets the look at its picture.</summary>
    public void Picture(MediaAssetRecord asset)
    {
        if (Change is not null)
        {
            (Change.Image, Change.ImageAssetId) = (asset, asset.Id);
        }
        else if (Opening is not null)
        {
            (Opening.Image, Opening.ImageAssetId) = (asset, asset.Id);
        }
    }
}
