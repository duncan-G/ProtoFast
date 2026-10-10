using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// Edits a picture by the author's instructions, from the picture and the one reference they picked, if
/// any, and stores the edit over it.
/// It stays current for what it pictures; pictures composed from it go stale, as after a redraw, and
/// a redraw starts again from the description. The provider is called outside the story lock.
/// </summary>
public sealed class ImageRefiner(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    IImageGenerator generator,
    ImageAssets assets,
    ILogger<ImageRefiner> logger)
{
    public const int InstructionsMaxLength = 2000;

    public static string Instructions(string instructions)
    {
        var trimmed = instructions.Trim();
        if (trimmed.Length == 0)
        {
            throw StoryErrors.Invalid("Say what to change in the picture.");
        }

        return trimmed.Length <= InstructionsMaxLength
            ? trimmed
            : throw StoryErrors.Invalid($"What to change is limited to {InstructionsMaxLength} characters.");
    }

    /// <param name="size">The size the picture was drawn at.</param>
    /// <param name="referenceAssetId">Another picture of the story's to take what the change needs from.</param>
    public async Task RefineAsync(
        Guid storyId, Guid assetId, string size, string instructions, Guid? referenceAssetId, CancellationToken ct)
    {
        var change = Instructions(instructions);
        if (referenceAssetId == assetId)
        {
            throw StoryErrors.Invalid("Pick another picture as the reference.");
        }

        ImageRequest request;
        string contentHash;
        using (unitOfWorkFactory.CreateReadOnly(nameof(RefineAsync)))
        {
            var asset = await assets.GetAsync(storyId, assetId, ct) ?? throw new ImageException(Gone);
            contentHash = asset.ContentHash;
            if (referenceAssetId is not { } referenceId)
            {
                var current = await assets.ReadAsync(asset, ImagePrompts.RefineCaption, ct) ?? throw new ImageException(Gone);
                request = new ImageRequest(size, "", [current], ImagePrompts.Refine(change));
            }
            else
            {
                var current = await assets.ReadAsync(asset, ImagePrompts.RefineWithReferenceCaption, ct) ?? throw new ImageException(Gone);
                var referenceAsset = await assets.GetAsync(storyId, referenceId, ct)
                                     ?? throw StoryErrors.NotFound(ReferenceNotFound);
                var reference = await assets.ReadAsync(referenceAsset, ImagePrompts.RefineReferenceCaption, ct)
                                ?? throw new ImageException("The reference picture is missing from storage; pick another.");
                request = new ImageRequest(size, "", [current, reference], ImagePrompts.RefineWithReference(change));
            }
        }

        var image = await generator.GenerateAsync(request, ct);

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(RefineAsync));
        await storyScope.LockAsync(storyId, ct);
        var stored = await assets.GetAsync(storyId, assetId, ct) ?? throw new ImageException(Gone);
        if (stored.ContentHash != contentHash)
        {
            throw new ImageException("The picture was drawn again while it was being refined; refine the new one.");
        }

        await assets.ReplaceAsync(stored, request, image, ct);
        await unitOfWork.CommitAsync(ct);
        logger.LogInformation("Refined picture {Asset} of story {Story} with {Provider}/{Model}", assetId, storyId, generator.Provider, generator.Model);
    }

    public const string ReferenceNotFound = "That reference picture is not in the story.";

    private const string Gone = "The picture is missing from storage; picture it again first.";
}
