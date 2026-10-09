using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// A picture is stale when what it is of has changed (its compiled prompt hashes differently), and a
/// frame also when a library picture it was offered has been rendered again since. Frames offered
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

    public bool Frame(FrameContents frame, MediaAssetRecord asset, StoryRecord story) =>
        Differs(asset, options.FrameSize, ImagePrompts.Frame(frame, options.Style)) || LibraryMovedOn(asset, story);

    private bool Differs(MediaAssetRecord asset, string size, string prompt) =>
        ImageRequests.Hash(generator, size, prompt) != asset.RequestHash;

    private static bool LibraryMovedOn(MediaAssetRecord asset, StoryRecord story) =>
        asset.References.Any(reference =>
        {
            var current = reference.Source switch
            {
                ReferenceSources.Character => story.Characters.FirstOrDefault(c => c.Id == reference.SubjectId)?.Image,
                ReferenceSources.Location => story.Locations.FirstOrDefault(l => l.Id == reference.SubjectId)?.Image,
                ReferenceSources.Prop => story.Props.FirstOrDefault(p => p.Id == reference.SubjectId)?.Image,
                _ => null,
            };
            return current is not null && current.ContentHash != reference.ContentHash;
        });
}
