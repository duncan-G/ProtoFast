using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using LocationVariationRecord = ProtoFast.Data.ThePlot.Entities.LocationVariation;
using MediaAssetKindRecord = ProtoFast.Data.ThePlot.Entities.MediaAssetKind;
using MediaReferenceRecord = ProtoFast.Data.ThePlot.Entities.MediaReference;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// Pictures a library entry from its sheet alone: a character's reference sheet, a location's empty
/// establishing shot, a prop on its own, extras together. These are what frames are kept consistent with.
/// A part of a location is drawn from the location's picture, when it has one, so both show one place.
/// </summary>
public sealed class LibraryImageGenerator(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    IStoryRepository storyRepository,
    IQueryFactory<StoryRecord, IStoryQuery> storyQueries,
    IImageGenerator generator,
    ImageStaleness staleness,
    ImageAssets assets,
    ImageRefiner refiner,
    ImagingOptions options,
    ILogger<LibraryImageGenerator> logger)
{
    /// <summary>Edits a library entry's picture by the author's instructions and reference; frames composed from it go stale.</summary>
    /// <param name="storyId">The subject's story, which the caller has checked it belongs to.</param>
    /// <returns>The story's library as stored after the write.</returns>
    public async Task<StoryRecord> RefineAsync(
        Guid storyId, ReferenceSource source, Guid subjectId, string instructions, Guid? referenceAssetId, CancellationToken ct)
    {
        Guid assetId;
        string size;
        using (unitOfWorkFactory.CreateReadOnly(nameof(RefineAsync)))
        {
            var (_, drawnAt, current, _, _) = Describe(await LoadAsync(storyId, ct), source, subjectId);
            assetId = current?.Id ?? throw new ImageException("It has no picture to refine yet; picture it first.");
            size = drawnAt;
        }

        await refiner.RefineAsync(storyId, assetId, size, instructions, referenceAssetId, ct);
        using (unitOfWorkFactory.CreateReadOnly(nameof(RefineAsync)))
        {
            return await LoadAsync(storyId, ct);
        }
    }

    /// <param name="storyId">The subject's story, which the caller has checked it belongs to.</param>
    /// <returns>The story's library as stored after the write.</returns>
    public async Task<StoryRecord> GenerateAsync(
        Guid storyId, ReferenceSource source, Guid subjectId, bool regenerate, CancellationToken ct)
    {
        string requestHash;
        ImageRequest? request = null;
        GeneratedImage? image = null;
        List<MediaReferenceRecord> references = [];
        using (unitOfWorkFactory.CreateReadOnly(nameof(GenerateAsync)))
        {
            var story = await LoadAsync(storyId, ct);
            var (prompt, size, current, stale, drawnFrom) = Describe(story, source, subjectId);
            requestHash = drawnFrom is null
                ? ImageRequests.Hash(generator, size, prompt)
                : staleness.LocationVariationHash(drawnFrom.Value.Location, drawnFrom.Value.Variation);
            if (!regenerate && current is not null && !stale)
            {
                return story;
            }

            if (regenerate || await assets.FindAsync(storyId, MediaAssetKindRecord.LibraryImage, requestHash, ct) is null)
            {
                if (drawnFrom?.Location.Image is { } plate)
                {
                    var input = await assets.ReadAsync(plate, ImagePrompts.LocationVariationCaption, ct)
                                ?? throw new ImageException($"{drawnFrom.Value.Location.Name}'s picture is missing from storage; picture it again first.");
                    references.Add(new MediaReferenceRecord
                    {
                        AssetId = plate.Id,
                        ContentHash = plate.ContentHash,
                        Source = ReferenceSources.Location,
                        SubjectId = drawnFrom.Value.Location.Id,
                        Label = drawnFrom.Value.Location.Name,
                    });
                    request = new ImageRequest(size, "", [input], prompt);
                }
                else
                {
                    request = ImagePrompts.Request(size, prompt, []);
                }

                image = await generator.GenerateAsync(request, ct);
                logger.LogInformation("Pictured {Source} {Subject} with {Provider}/{Model}", source, subjectId, generator.Provider, generator.Model);
            }
        }

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(GenerateAsync));
        await storyScope.LockAsync(storyId, ct);
        var asset = request is null || image is null
            ? await assets.FindAsync(storyId, MediaAssetKindRecord.LibraryImage, requestHash, ct)
            : await assets.StoreAsync(storyId, MediaAssetKindRecord.LibraryImage, requestHash, request, image, references, ct);
        var stored = await LoadAsync(storyId, ct);
        if (asset is not null)
        {
            switch (source)
            {
                case ReferenceSource.Character when stored.Characters.FirstOrDefault(c => c.Id == subjectId) is { } character:
                    (character.Image, character.ImageAssetId) = (asset, asset.Id);
                    break;
                case ReferenceSource.Location when stored.Locations.FirstOrDefault(l => l.Id == subjectId) is { } location:
                    (location.Image, location.ImageAssetId) = (asset, asset.Id);
                    break;
                case ReferenceSource.Prop when stored.Props.FirstOrDefault(p => p.Id == subjectId) is { } prop:
                    (prop.Image, prop.ImageAssetId) = (asset, asset.Id);
                    break;
                case ReferenceSource.Extra when stored.Extras.FirstOrDefault(e => e.Id == subjectId) is { } extra:
                    (extra.Image, extra.ImageAssetId) = (asset, asset.Id);
                    break;
                case ReferenceSource.LocationVariation
                    when stored.Locations.SelectMany(l => l.Variations).FirstOrDefault(v => v.Id == subjectId) is { } variation:
                    (variation.Image, variation.ImageAssetId) = (asset, asset.Id);
                    break;
            }
        }

        await unitOfWork.CommitAsync(ct);
        return stored;
    }

    /// <returns>With <c>DrawnFrom</c> for a part of a location, whose picture is drawn from the location's.</returns>
    private (string Prompt, string Size, Data.ThePlot.Entities.MediaAsset? Current, bool Stale,
        (LocationRecord Location, LocationVariationRecord Variation)? DrawnFrom) Describe(
        StoryRecord story, ReferenceSource source, Guid subjectId)
    {
        switch (source)
        {
            case ReferenceSource.Character:
            {
                var character = story.Characters.FirstOrDefault(c => c.Id == subjectId)
                                ?? throw StoryErrors.NotFound(LibraryErrors.CharacterNotFound);
                return (ImagePrompts.Character(character, options.Style), options.CharacterSize, character.Image,
                    character.Image is { } image && staleness.Character(character, image), null);
            }
            case ReferenceSource.Location:
            {
                var location = story.Locations.FirstOrDefault(l => l.Id == subjectId)
                               ?? throw StoryErrors.NotFound(LibraryErrors.LocationNotFound);
                return (ImagePrompts.Location(location, options.Style), options.LocationSize, location.Image,
                    location.Image is { } image && staleness.Location(location, image), null);
            }
            case ReferenceSource.LocationVariation:
            {
                var location = story.Locations.FirstOrDefault(l => l.Variations.Any(v => v.Id == subjectId))
                               ?? throw StoryErrors.NotFound(LibraryErrors.LocationVariationNotFound);
                var variation = location.Variations.First(v => v.Id == subjectId);
                return (staleness.LocationVariationPrompt(location, variation), options.LocationSize, variation.Image,
                    variation.Image is { } image && staleness.LocationVariation(location, variation, image), (location, variation));
            }
            case ReferenceSource.Prop:
            {
                var prop = story.Props.FirstOrDefault(p => p.Id == subjectId)
                           ?? throw StoryErrors.NotFound(LibraryErrors.PropNotFound);
                return (ImagePrompts.Prop(prop, options.Style), options.PropSize, prop.Image,
                    prop.Image is { } image && staleness.Prop(prop, image), null);
            }
            case ReferenceSource.Extra:
            {
                var extra = story.Extras.FirstOrDefault(e => e.Id == subjectId)
                            ?? throw StoryErrors.NotFound(LibraryErrors.ExtraNotFound);
                return (ImagePrompts.Extra(extra, options.Style), options.ExtraSize, extra.Image,
                    extra.Image is { } image && staleness.Extra(extra, image), null);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    private async Task<StoryRecord> LoadAsync(Guid storyId, CancellationToken ct) =>
        await storyRepository.GetFirstByQueryAsync(storyQueries.Create().WithId(storyId).WithLibrary(), ct)
        ?? throw StoryErrors.NotFound(StoryErrors.StoryNotFound);
}
