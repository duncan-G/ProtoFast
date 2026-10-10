using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Database.Abstractions;
using MediaAssetKindRecord = ProtoFast.Data.ThePlot.Entities.MediaAssetKind;
using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;
using MediaReferenceRecord = ProtoFast.Data.ThePlot.Entities.MediaReference;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// Pictures a character in a look other than the library's: the same reference sheet, composed from
/// the library's picture of them and of what they carry. Every frame in that look takes it as their
/// reference, in this scene and in the next ones it carries into, since the same look compiles to
/// the same prompt and the media cache returns the same picture. The library picture is made first
/// when missing, or the look would become who the character is.
/// </summary>
public sealed class LookImageGenerator(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    DirectionScope directionScope,
    LibraryImageGenerator libraryImages,
    IImageGenerator generator,
    ImageStaleness staleness,
    ImageAssets assets,
    ImageRefiner refiner,
    ImagingOptions options,
    ILogger<LookImageGenerator> logger)
{
    /// <summary>Edits a look's picture by the author's instructions and reference; every row and frame in that look follows.</summary>
    /// <param name="elementId">The look change's line; null is the look the scene opens with.</param>
    /// <returns>The scene with its direction, as stored after the write.</returns>
    public async Task<DirectedScene> RefineAsync(
        Guid sceneId, Guid characterId, Guid? elementId, string instructions, Guid? referenceAssetId, CancellationToken ct)
    {
        Guid storyId, assetId;
        using (unitOfWorkFactory.CreateReadOnly(nameof(RefineAsync)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
            storyId = directed.Story.Id;
            var row = Row(directed.Scene, characterId, elementId);
            assetId = row.Image?.Id ?? throw new ImageException("That look has no picture to refine yet; picture it first.");
        }

        await refiner.RefineAsync(storyId, assetId, options.CharacterSize, instructions, referenceAssetId, ct);
        using (unitOfWorkFactory.CreateReadOnly(nameof(RefineAsync)))
        {
            return await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
        }
    }

    /// <summary>Pictures the looks of everyone on screen in the frame that have no current picture.</summary>
    public async Task PictureFrameAsync(Guid sceneId, Guid frameId, CancellationToken ct)
    {
        List<(Guid CharacterId, Guid? ElementId)> missing;
        using (unitOfWorkFactory.CreateReadOnly(nameof(PictureFrameAsync)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
            var frame = FrameContents.ForScene(directed).FirstOrDefault(f => f.Frame.Id == frameId)
                        ?? throw StoryErrors.NotFound(DirectionErrors.FrameNotFound);
            missing = frame.OnScreen
                .Where(c => !c.Look.Look.IsSheet(c.Character)
                            && (c.Look.Image is not { } image || staleness.Look(c.Character, c.Look.Look, directed.Story, image)))
                .Select(c => (c.Character.Id, c.Look.Change?.ElementId))
                .ToList();
        }

        foreach (var (characterId, elementId) in missing)
        {
            await GenerateAsync(sceneId, characterId, elementId, regenerate: false, ct);
        }
    }

    /// <param name="elementId">The look change's line; null is the look the scene opens with.</param>
    /// <returns>The scene with its direction, as stored after the write.</returns>
    public async Task<DirectedScene> GenerateAsync(Guid sceneId, Guid characterId, Guid? elementId, bool regenerate, CancellationToken ct)
    {
        Guid storyId;
        bool sheetMissing;
        using (unitOfWorkFactory.CreateReadOnly(nameof(GenerateAsync)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
            storyId = directed.Story.Id;
            sheetMissing = directed.Story.Characters.FirstOrDefault(c => c.Id == characterId) is { Image: null };
        }

        if (sheetMissing)
        {
            await libraryImages.GenerateAsync(storyId, ReferenceSource.Character, characterId, regenerate: false, ct);
        }

        string requestHash;
        ImageRequest? request = null;
        GeneratedImage? image = null;
        List<MediaReferenceRecord> references = [];
        using (unitOfWorkFactory.CreateReadOnly(nameof(GenerateAsync)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
            var character = directed.Story.Characters.FirstOrDefault(c => c.Id == characterId)
                            ?? throw StoryErrors.NotFound(LibraryErrors.CharacterNotFound);
            var row = Row(directed.Scene, characterId, elementId);
            if (row.Look.IsSheet(character))
            {
                throw StoryErrors.Invalid($"That look is {character.Name}'s library look; picture it in the library.");
            }

            var prompt = staleness.LookPrompt(character, row.Look, directed.Story);
            requestHash = ImageRequests.Hash(generator, options.CharacterSize, prompt);
            if (!regenerate && row.Image is { } current && !staleness.Look(character, row.Look, directed.Story, current))
            {
                return directed;
            }

            var cached = await assets.FindAsync(storyId, MediaAssetKindRecord.LookImage, requestHash, ct);
            if (regenerate || cached is null || staleness.Look(character, row.Look, directed.Story, cached))
            {
                List<ImageInput> inputs = [];
                var candidates = new List<(ReferenceSource Source, Guid SubjectId, MediaAssetRecord Asset, string Name, string Caption)>();
                if (character.Image is { } sheet)
                {
                    candidates.Add((ReferenceSource.Character, character.Id, sheet, character.Name,
                        $"The library's reference sheet of {character.Name} in their usual look. Keep the face, hair, build and marks exactly; change only what the description changes."));
                }

                foreach (var prop in ImageStaleness.Holding(row.Look, directed.Story).Where(p => p.Image is not null))
                {
                    candidates.Add((ReferenceSource.Prop, prop.Id, prop.Image!, prop.Name,
                        $"The library's photograph of {prop.Name}, which {character.Name} carries. It must be this object, at this size."));
                }

                foreach (var candidate in candidates)
                {
                    if (await assets.ReadAsync(candidate.Asset, $"Image {inputs.Count + 1}: {candidate.Caption}", ct) is not { } input)
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

                request = ImagePrompts.Request(options.CharacterSize, prompt, inputs);
                image = await generator.GenerateAsync(request, ct);
                logger.LogInformation("Pictured a look of {Character} in {Scene} with {Provider}/{Model} from {Count} references",
                    characterId, sceneId, generator.Provider, generator.Model, references.Count);
            }
        }

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(GenerateAsync));
        await storyScope.LockAsync(storyId, ct);
        var asset = request is null || image is null
            ? await assets.FindAsync(storyId, MediaAssetKindRecord.LookImage, requestHash, ct)
            : await assets.StoreAsync(storyId, MediaAssetKindRecord.LookImage, requestHash, request, image, references, ct);
        var stored = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
        if (asset is not null)
        {
            // Every row of the scene in the same look shares the picture.
            var look = Row(stored.Scene, characterId, elementId).Look;
            foreach (var other in Rows(stored.Scene, characterId).Where(r => r.Look.SameAs(look)))
            {
                other.Picture(asset);
            }
        }

        await unitOfWork.CommitAsync(ct);
        return stored;
    }

    private static LookInForce Row(SceneRecord scene, Guid characterId, Guid? elementId)
    {
        if (elementId is { } id)
        {
            var change = scene.LookChanges.FirstOrDefault(c => c.CharacterId == characterId && c.ElementId == id)
                         ?? throw StoryErrors.NotFound("That look change is no longer in the scene.");
            return new LookInForce(Look.Of(change), null, change);
        }

        var opening = scene.Appearances.FirstOrDefault(a => a.CharacterId == characterId)
                      ?? throw StoryErrors.NotFound("That character has no look in this scene.");
        return new LookInForce(Look.Of(opening), opening, null);
    }

    private static IEnumerable<LookInForce> Rows(SceneRecord scene, Guid characterId) =>
        scene.Appearances.Where(a => a.CharacterId == characterId).Select(a => new LookInForce(Look.Of(a), a, null))
            .Concat(scene.LookChanges.Where(c => c.CharacterId == characterId).Select(c => new LookInForce(Look.Of(c), null, c)));
}
