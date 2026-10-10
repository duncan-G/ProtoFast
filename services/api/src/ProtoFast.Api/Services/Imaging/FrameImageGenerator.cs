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
/// A variation is the previous frame's picture edited by its change alone; when its camera moves, that
/// picture is offered with the pictures of whatever comes into view and of the place, which at another
/// part of it is that part's picture. The provider is called outside the story lock.
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
    ImageRefiner refiner,
    ImagingOptions options,
    ILogger<FrameImageGenerator> logger)
{
    /// <summary>Edits the frame's picture by the author's instructions and the reference they picked, if any.</summary>
    /// <returns>The scene with its direction, as stored after the write; a locked frame is left as it is.</returns>
    public async Task<DirectedScene> RefineAsync(
        Guid sceneId, Guid frameId, string instructions, Guid? referenceAssetId, CancellationToken ct)
    {
        Guid storyId, assetId;
        using (unitOfWorkFactory.CreateReadOnly(nameof(RefineAsync)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
            var frame = directed.Scene.Frames.FirstOrDefault(f => f.Id == frameId)
                        ?? throw StoryErrors.NotFound(DirectionErrors.FrameNotFound);
            if (frame.Locked)
            {
                return directed;
            }

            storyId = directed.Story.Id;
            assetId = frame.ImageAssetId ?? throw new ImageException("The frame has no picture to refine yet; picture it first.");
        }

        await refiner.RefineAsync(storyId, assetId, options.FrameSize, instructions, referenceAssetId, ct);
        using (unitOfWorkFactory.CreateReadOnly(nameof(RefineAsync)))
        {
            return await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
        }
    }

    /// <param name="regenerate">Render again even when the picture is current.</param>
    /// <returns>The scene with its direction, as stored after the write; a locked frame is left as it is.</returns>
    public async Task<DirectedScene> GenerateAsync(Guid sceneId, Guid frameId, bool regenerate, CancellationToken ct)
    {
        bool edit;
        using (unitOfWorkFactory.CreateReadOnly(nameof(GenerateAsync)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
            var shown = FrameContents.ForScene(directed).FirstOrDefault(f => f.Frame.Id == frameId);
            if (shown?.Frame.Locked == true)
            {
                return directed;
            }

            edit = shown is { VariationOf: not null, MovesCamera: false };
        }

        if (!edit)
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
            List<ImageInput> inputs = [];
            async Task OfferAsync(ReferenceCandidate candidate)
            {
                if (await assets.ReadAsync(candidate.Asset, ImagePrompts.Caption(inputs.Count + 1, candidate), ct) is not { } input)
                {
                    return;
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

            if (redo && frame.VariationOf is { } previous)
            {
                var moves = frame.MovesCamera;
                inputs.Add(await assets.ReadAsync(previous.Frame.Image!, ImagePrompts.VariationCaption(frame, previous), ct)
                           ?? throw new ImageException($"Frame {previous.Number}'s picture is missing from storage; picture it again first."));
                references.Add(new MediaReferenceRecord
                {
                    AssetId = previous.Frame.Image!.Id,
                    ContentHash = previous.Frame.Image.ContentHash,
                    Source = ReferenceSources.Frame,
                    SubjectId = previous.Frame.Id,
                    Label = $"Frame {previous.Number}",
                });

                // Whoever stays is drawn from the previous picture; only what comes into view, and the
                // place the camera moves through, are offered on their own.
                if (moves)
                {
                    var arrived = frame.NewSince(previous.Pictured);
                    foreach (var candidate in collector.Collect(directed.Story, frame, scene, earlier)
                                 .Where(c => c.Source is ReferenceSource.Location or ReferenceSource.LocationVariation
                                             || (c.Source != ReferenceSource.Frame && arrived.Contains(c.SubjectId)))
                                 .Take(options.MaxReferences - 1))
                    {
                        await OfferAsync(candidate);
                    }
                }

                request = new ImageRequest(options.FrameSize, "", inputs, staleness.VariationPrompt(frame));
                image = await generator.GenerateAsync(request, ct);
                logger.LogInformation("Pictured frame {Frame} of {Scene} with {Provider}/{Model} based off frame {Previous} ({Mode}): {References}",
                    frameId, sceneId, generator.Provider, generator.Model, previous.Number, moves ? "camera moves" : "same shot",
                    string.Join(", ", references.Select(r => r.Label)));
            }
            else if (redo)
            {
                foreach (var candidate in collector.Collect(directed.Story, frame, scene, earlier).Take(options.MaxReferences))
                {
                    await OfferAsync(candidate);
                }

                request = ImagePrompts.Request(options.FrameSize, ImagePrompts.Frame(frame, options.Style), inputs);
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
