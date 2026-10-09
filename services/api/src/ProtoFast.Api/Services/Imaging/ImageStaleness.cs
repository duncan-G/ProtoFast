using ProtoFast.Api.Services.Directing;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using ExtraRecord = ProtoFast.Data.ThePlot.Entities.Extra;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// A picture is stale when what it is of has changed (its compiled prompt hashes differently), and a
/// frame or look also when a library or look picture it was offered has been rendered again since. Frames offered
/// as references don't carry staleness on, or one new frame would stale the whole story.
/// </summary>
public sealed class ImageStaleness(IImageGenerator generator, ImagingOptions options)
{
    public bool Character(CharacterRecord character, MediaAssetRecord asset) =>
        Differs(asset, options.CharacterSize, ImagePrompts.Character(character, options.Style));

    public bool Location(LocationRecord location, MediaAssetRecord asset) =>
        Differs(asset, options.LocationSize, ImagePrompts.Location(location, options.Style));

    public bool Prop(PropRecord prop, MediaAssetRecord asset) =>
        Differs(asset, options.PropSize, ImagePrompts.Prop(prop, options.Style));

    public bool Extra(ExtraRecord extra, MediaAssetRecord asset) =>
        Differs(asset, options.ExtraSize, ImagePrompts.Extra(extra, options.Style));

    public bool Frame(FrameContents frame, MediaAssetRecord asset, StoryRecord story) =>
        Differs(asset, options.FrameSize, ImagePrompts.Frame(frame, options.Style)) || LibraryMovedOn(asset, story, frame.Scene);

    public bool Look(CharacterRecord character, Look look, StoryRecord story, MediaAssetRecord asset) =>
        Differs(asset, options.CharacterSize, LookPrompt(character, look, story)) || LibraryMovedOn(asset, story, null);

    public string LookPrompt(CharacterRecord character, Look look, StoryRecord story) =>
        ImagePrompts.Look(character, look, Holding(look, story), options.Style);

    public static IReadOnlyList<PropRecord> Holding(Look look, StoryRecord story) =>
        look.Holding.Select(id => story.Props.FirstOrDefault(p => p.Id == id)).OfType<PropRecord>().ToList();

    private bool Differs(MediaAssetRecord asset, string size, string prompt) =>
        ImageRequests.Hash(generator, size, prompt) != asset.RequestHash;

    /// <param name="scene">Where the look pictures a frame was offered are linked; null skips them.</param>
    private static bool LibraryMovedOn(MediaAssetRecord asset, StoryRecord story, SceneRecord? scene) =>
        asset.References.Any(reference =>
        {
            var current = reference.Source switch
            {
                ReferenceSources.Character => story.Characters.FirstOrDefault(c => c.Id == reference.SubjectId)?.Image,
                ReferenceSources.Location => story.Locations.FirstOrDefault(l => l.Id == reference.SubjectId)?.Image,
                ReferenceSources.Prop => story.Props.FirstOrDefault(p => p.Id == reference.SubjectId)?.Image,
                ReferenceSources.Extra => story.Extras.FirstOrDefault(e => e.Id == reference.SubjectId)?.Image,
                ReferenceSources.Look => scene?.Appearances.Select(a => a.Image).Concat(scene.LookChanges.Select(c => c.Image))
                    .FirstOrDefault(i => i?.Id == reference.AssetId),
                _ => null,
            };
            return current is not null && current.ContentHash != reference.ContentHash;
        });
}
