using ProtoFast.Api.Services.Directing;
using ProtoFast.Storage.Abstractions;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;
using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>Pictures as the client shows them: a presigned URL, and whether what they picture has moved on.</summary>
public sealed class ImageMessages(ImageStaleness staleness, IPresignedUrlFactory urls)
{
    public Func<FrameRecord, MediaImage?> ForFrames(DirectedScene directed)
    {
        var contents = directed.Scene.Frames.Any(f => f.Image is not null)
            ? FrameContents.ForScene(directed).ToDictionary(f => f.Frame.Id)
            : [];
        return frame => frame.Image is { } image && contents.TryGetValue(frame.Id, out var shown)
            ? ToMessage(image, staleness.Frame(shown, image, directed.Story))
            : null;
    }

    public IEnumerable<LibraryImage> ForLibrary(StoryRecord story) =>
        story.Characters.Where(c => c.Image is not null)
            .Select(c => Library(ReferenceSources.Character, c.Id, c.Image!, staleness.Character(c, c.Image!)))
            .Concat(story.Locations.Where(l => l.Image is not null)
                .Select(l => Library(ReferenceSources.Location, l.Id, l.Image!, staleness.Location(l, l.Image!))))
            .Concat(story.Props.Where(p => p.Image is not null)
                .Select(p => Library(ReferenceSources.Prop, p.Id, p.Image!, staleness.Prop(p, p.Image!))));

    private LibraryImage Library(string kind, Guid id, MediaAssetRecord image, bool stale) => new()
    {
        SubjectKind = kind,
        SubjectId = id.ToString(),
        Image = ToMessage(image, stale),
    };

    private MediaImage ToMessage(MediaAssetRecord image, bool stale)
    {
        var message = new MediaImage
        {
            AssetId = image.Id.ToString(),
            Url = urls.PresignGet(image.StorageKey),
            ContentType = image.ContentType,
            Stale = stale,
            Notes = image.Notes ?? "",
        };
        message.References.AddRange(image.References.Select(r => r.Label));
        return message;
    }
}
