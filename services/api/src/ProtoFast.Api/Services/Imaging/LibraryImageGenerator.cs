using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using MediaAssetKindRecord = ProtoFast.Data.ThePlot.Entities.MediaAssetKind;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// Pictures a library entry from its sheet alone: a character's reference sheet, a location's empty
/// establishing shot, a prop on its own. These are what frames are kept consistent with.
/// </summary>
public sealed class LibraryImageGenerator(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    IStoryRepository storyRepository,
    IQueryFactory<StoryRecord, IStoryQuery> storyQueries,
    IImageGenerator generator,
    ImageStaleness staleness,
    ImageAssets assets,
    ImagingOptions options,
    ILogger<LibraryImageGenerator> logger)
{
    /// <param name="storyId">The subject's story, which the caller has checked it belongs to.</param>
    /// <returns>The story's library as stored after the write.</returns>
    public async Task<StoryRecord> GenerateAsync(
        Guid storyId, ReferenceSource source, Guid subjectId, bool regenerate, CancellationToken ct)
    {
        string requestHash;
        ImageRequest? request = null;
        GeneratedImage? image = null;
        using (unitOfWorkFactory.CreateReadOnly(nameof(GenerateAsync)))
        {
            var story = await LoadAsync(storyId, ct);
            var (prompt, size, current, stale) = Describe(story, source, subjectId);
            requestHash = ImageRequests.Hash(generator, size, prompt);
            if (!regenerate && current is not null && !stale)
            {
                return story;
            }

            if (regenerate || await assets.FindAsync(storyId, MediaAssetKindRecord.LibraryImage, requestHash, ct) is null)
            {
                request = ImagePrompts.Request(size, prompt, []);
                image = await generator.GenerateAsync(request, ct);
                logger.LogInformation("Pictured {Source} {Subject} with {Provider}/{Model}", source, subjectId, generator.Provider, generator.Model);
            }
        }

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(GenerateAsync));
        await storyScope.LockAsync(storyId, ct);
        var asset = request is null || image is null
            ? await assets.FindAsync(storyId, MediaAssetKindRecord.LibraryImage, requestHash, ct)
            : await assets.StoreAsync(storyId, MediaAssetKindRecord.LibraryImage, requestHash, request, image, [], ct);
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
            }
        }

        await unitOfWork.CommitAsync(ct);
        return stored;
    }

    private (string Prompt, string Size, Data.ThePlot.Entities.MediaAsset? Current, bool Stale) Describe(
        StoryRecord story, ReferenceSource source, Guid subjectId)
    {
        switch (source)
        {
            case ReferenceSource.Character:
            {
                var character = story.Characters.FirstOrDefault(c => c.Id == subjectId)
                                ?? throw StoryErrors.NotFound(LibraryErrors.CharacterNotFound);
                return (ImagePrompts.Character(character, options.Style), options.CharacterSize, character.Image,
                    character.Image is { } image && staleness.Character(character, image));
            }
            case ReferenceSource.Location:
            {
                var location = story.Locations.FirstOrDefault(l => l.Id == subjectId)
                               ?? throw StoryErrors.NotFound(LibraryErrors.LocationNotFound);
                return (ImagePrompts.Location(location, options.Style), options.LocationSize, location.Image,
                    location.Image is { } image && staleness.Location(location, image));
            }
            case ReferenceSource.Prop:
            {
                var prop = story.Props.FirstOrDefault(p => p.Id == subjectId)
                           ?? throw StoryErrors.NotFound(LibraryErrors.PropNotFound);
                return (ImagePrompts.Prop(prop, options.Style), options.PropSize, prop.Image,
                    prop.Image is { } image && staleness.Prop(prop, image));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    private async Task<StoryRecord> LoadAsync(Guid storyId, CancellationToken ct) =>
        await storyRepository.GetFirstByQueryAsync(storyQueries.Create().WithId(storyId).WithLibrary(), ct)
        ?? throw StoryErrors.NotFound(StoryErrors.StoryNotFound);
}
