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
/// A variation is the previous frame's picture edited by its change alone. The provider is called
/// outside the story lock.
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
        bool variation;
        using (unitOfWorkFactory.CreateReadOnly(nameof(GenerateAsync)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
            var shown = FrameContents.ForScene(directed).FirstOrDefault(f => f.Frame.Id == frameId);
            if (shown?.Frame.Locked == true)
            {
                return directed;
            }

            variation = shown?.VariationOf is not null;
        }

        if (!variation)
        {
            await lookImages.PictureFrameAsync(sceneId, frameId, ct);
        }

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
            requestHash = staleness.FrameHash(frame);

            // The media cache: the same picture was made before, and nothing it was made from has moved on.
            var cached = await assets.FindAsync(storyId, MediaAssetKindRecord.FrameImage, requestHash, ct);
            var redo = regenerate || cached is null || staleness.Frame(frame, cached, directed.Story);
            if (redo && frame.VariationOf is { } previous)
            {
                var input = await assets.ReadAsync(previous.Frame.Image!, ImagePrompts.VariationCaption(previous), ct)
                            ?? throw new ImageException($"Frame {previous.Number}'s picture is missing from storage; picture it again first.");
                references.Add(new MediaReferenceRecord
                {
                    AssetId = previous.Frame.Image!.Id,
                    ContentHash = previous.Frame.Image.ContentHash,
                    Source = ReferenceSources.Frame,
                    SubjectId = previous.Frame.Id,
                    Label = $"Frame {previous.Number}",
                });
                request = ImagePrompts.VariationRequest(options.FrameSize, frame, input);
                image = await generator.GenerateAsync(request, ct);
                logger.LogInformation("Pictured frame {Frame} of {Scene} with {Provider}/{Model} as a variation of frame {Previous}",
                    frameId, sceneId, generator.Provider, generator.Model, previous.Number);
            }
            else if (redo)
            {
                var prompt = ImagePrompts.Frame(frame, options.Style);
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
