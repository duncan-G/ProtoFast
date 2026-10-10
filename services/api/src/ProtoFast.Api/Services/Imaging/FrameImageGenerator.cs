using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Database.Abstractions;
using MediaAssetKindRecord = ProtoFast.Data.ThePlot.Entities.MediaAssetKind;
using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;
using MediaReferenceRecord = ProtoFast.Data.ThePlot.Entities.MediaReference;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// Pictures one frame. Its prompt is compiled from what it shows; the pictures that already exist
/// around it (each character's look, library, neighbouring frames, earlier scenes) are offered to
/// Muse with a caption each, and Muse decides what to take from them. The looks of everyone on screen
/// are pictured first, so a character carrying something is drawn from one picture in every frame.
/// The provider is called outside the story lock.
/// </summary>
public sealed class FrameImageGenerator(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    DirectionScope directionScope,
    LookImageGenerator lookImages,
    IImageGenerator generator,
    ReferenceCollector collector,
    ImageStaleness staleness,
    ImageAssets assets,
    ImagingOptions options,
    ILogger<FrameImageGenerator> logger)
{
    /// <param name="regenerate">Render again even when the picture is current.</param>
    /// <returns>The scene with its direction, as stored after the write; a locked frame is left as it is.</returns>
    public async Task<DirectedScene> GenerateAsync(Guid sceneId, Guid frameId, bool regenerate, CancellationToken ct)
    {
        using (unitOfWorkFactory.CreateReadOnly(nameof(GenerateAsync)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
            if (directed.Scene.Frames.FirstOrDefault(f => f.Id == frameId) is { Locked: true })
            {
                return directed;
            }
        }

        await lookImages.PictureFrameAsync(sceneId, frameId, ct);

        Guid storyId;
        string requestHash;
        ImageRequest? request = null;
        GeneratedImage? image = null;
        List<MediaReferenceRecord> references = [];
        using (unitOfWorkFactory.CreateReadOnly(nameof(GenerateAsync)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: true, ct);
            storyId = directed.Story.Id;
            var (scene, earlier) = ReferenceCollector.Resolve(directed);
            var frame = scene.FirstOrDefault(f => f.Frame.Id == frameId)
                        ?? throw StoryErrors.NotFound(DirectionErrors.FrameNotFound);
            var prompt = ImagePrompts.Frame(frame, options.Style);
            requestHash = ImageRequests.Hash(generator, options.FrameSize, prompt);

            // The media cache: the same picture was made before, and nothing it was made from has moved on.
            var cached = await assets.FindAsync(storyId, MediaAssetKindRecord.FrameImage, requestHash, ct);
            if (regenerate || cached is null || staleness.Frame(frame, cached, directed.Story))
            {
                List<ImageInput> inputs = [];
                foreach (var candidate in collector.Collect(directed.Story, frame, scene, earlier).Take(options.MaxReferences))
                {
                    if (await assets.ReadAsync(candidate.Asset, ImagePrompts.Caption(inputs.Count + 1, candidate), ct) is not { } input)
                    {
                        continue;
                    }

                    inputs.Add(input);
                    references.Add(new MediaReferenceRecord
                    {
                        AssetId = candidate.Asset.Id,
                        ContentHash = candidate.Asset.ContentHash,
                        Source = ReferenceSources.Of(candidate.Source),
                        SubjectId = candidate.SubjectId,
                        Label = candidate.Name,
                    });
                }

                request = ImagePrompts.Request(options.FrameSize, prompt, inputs);
                image = await generator.GenerateAsync(request, ct);
                logger.LogInformation("Pictured frame {Frame} of {Scene} with {Provider}/{Model} from {Count} references: {References}",
                    frameId, sceneId, generator.Provider, generator.Model, references.Count, string.Join(", ", references.Select(r => r.Label)));
            }
        }

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(GenerateAsync));
        await storyScope.LockAsync(storyId, ct);
        MediaAssetRecord? asset = request is null || image is null
            ? await assets.FindAsync(storyId, MediaAssetKindRecord.FrameImage, requestHash, ct)
            : await assets.StoreAsync(storyId, MediaAssetKindRecord.FrameImage, requestHash, request, image, references, ct);
        var current = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
        if (asset is not null && current.Scene.Frames.FirstOrDefault(f => f.Id == frameId) is { } stored)
        {
            stored.Image = asset;
            stored.ImageAssetId = asset.Id;
        }

        await unitOfWork.CommitAsync(ct);
        return current;
    }
}
