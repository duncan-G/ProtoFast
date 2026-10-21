using System.Security.Cryptography;
using System.Text;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Imaging;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.Storage.Abstractions;
using ViewPictureRecord = ProtoFast.Data.ThePlot.Entities.ViewPicture;

namespace ProtoFast.Api.Services.Admin;

/// <summary>
/// Pictures a world's views and keeps them, as the story's writer whose rows they are. A view of a
/// place is drawn from the pictures already made of it, so every view shows the same set.
/// </summary>
public sealed class ViewPictures(
    UserContext userContext,
    IUnitOfWorkFactory unitOfWorkFactory,
    IViewPictureRepository pictureRepository,
    IQueryFactory<ViewPictureRecord, IViewPictureQuery> pictureQueries,
    IObjectStore objectStore,
    IImageGenerator generator,
    ILogger<ViewPictures> logger)
{
    private const int MaxReferences = 3;

    /// <exception cref="ImageException">Muse made no picture.</exception>
    internal async Task<(ViewPictureRecord Picture, byte[] Image)> PictureAsync(GetWorldReply world, WorldView view, CancellationToken ct)
    {
        using var writer = userContext.SetCurrentUser(world.OwnerId);
        var storyId = Guid.Parse(world.StoryId);
        var references = await ReferencesAsync(world, view, ct);
        var prompt = ViewPrompt.Compile(world, view, references.Select(r => r.View).ToList());
        var inputs = references.Select(r => r.Input).ToList();
        var image = await generator.GenerateAsync(prompt, inputs, ct);

        var hash = Convert.ToHexStringLower(SHA256.HashData(image.Bytes));
        var storageKey = $"stories/{storyId}/views/{hash}.{Extension(image.ContentType)}";
        await objectStore.WriteBytesAsync(storageKey, image.Bytes, image.ContentType, hash, ct);

        var key = SceneViews.KeyOf(world, view, world.Parts.ToDictionary(p => p.Id));
        string? replaced;
        ViewPictureRecord picture;
        using (var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(PictureAsync)))
        {
            var existing = await pictureRepository.GetFirstByQueryAsync(
                pictureQueries.Create().Of(key.SceneId, key.CameraLocationId, key.CameraPart, key.TowardLocationId, key.TowardPart, key.State), ct);
            picture = existing ?? new ViewPictureRecord
            {
                Id = Guid.NewGuid(),
                StoryId = storyId,
                SceneId = key.SceneId,
                CameraLocationId = key.CameraLocationId,
                CameraPart = key.CameraPart,
                TowardLocationId = key.TowardLocationId,
                TowardPart = key.TowardPart,
                State = key.State,
                StorageKey = storageKey,
                ContentType = image.ContentType,
                Prompt = "",
            };
            replaced = existing is not null && existing.StorageKey != storageKey ? existing.StorageKey : null;
            picture.StorageKey = storageKey;
            picture.ContentType = image.ContentType;
            picture.ByteSize = image.Bytes.Length;
            picture.Prompt = Transcript(inputs, prompt);
            picture.Notes = image.Notes;
            picture.CostUsdMicros = image.CostUsdMicros;
            if (existing is null)
            {
                await pictureRepository.AddAsync(picture, ct);
            }

            await unitOfWork.CommitAsync(ct);
        }

        if (replaced is not null)
        {
            await DeleteAsync(replaced);
        }

        return (picture, image.Bytes);
    }

    /// <summary>Null when the story has no such picture, or its bytes are gone.</summary>
    internal async Task<(byte[] Image, string ContentType)?> ReadAsync(string ownerId, Guid storyId, Guid pictureId, CancellationToken ct)
    {
        using var writer = userContext.SetCurrentUser(ownerId);
        ViewPictureRecord? picture;
        using (unitOfWorkFactory.CreateReadOnly(nameof(ReadAsync)))
        {
            picture = await pictureRepository.GetFirstByQueryAsync(pictureQueries.Create().InStory(storyId).WithId(pictureId), ct);
        }

        return picture is not null && await BytesAsync(picture.StorageKey, ct) is { } bytes ? (bytes, picture.ContentType) : null;
    }

    /// <summary>
    /// Other views of the same place that have a picture: this camera before the place changed
    /// first, then the scene's other views, then the nearest scenes'.
    /// </summary>
    private async Task<List<(WorldView View, ImageInput Input)>> ReferencesAsync(GetWorldReply world, WorldView view, CancellationToken ct)
    {
        var before = SceneViews.Before(world, view);
        var others = world.Views
            .Where(v => v != view && v.Picture is not null && v.Camera.LocationId == view.Camera.LocationId)
            .OrderBy(v => v != before)
            .ThenBy(v => Math.Abs(v.Scene - view.Scene))
            .ThenBy(v => v.Scene)
            .Take(MaxReferences)
            .ToList();
        if (others.Count == 0)
        {
            return [];
        }

        var ids = others.Select(v => Guid.Parse(v.Picture.Id)).ToHashSet();
        IReadOnlyList<ViewPictureRecord> rows;
        using (unitOfWorkFactory.CreateReadOnly(nameof(ReferencesAsync)))
        {
            rows = await pictureRepository.GetByQueryAsync(pictureQueries.Create().InStory(Guid.Parse(world.StoryId)), ct);
        }

        var keys = rows.Where(r => ids.Contains(r.Id)).ToDictionary(r => r.Id);
        var references = new List<(WorldView, ImageInput)>();
        foreach (var other in others)
        {
            if (keys.TryGetValue(Guid.Parse(other.Picture.Id), out var row) && await BytesAsync(row.StorageKey, ct) is { } bytes)
            {
                references.Add((other, new ImageInput(bytes, row.ContentType, ViewPrompt.Caption(world, other, other == before), row.StorageKey)));
            }
        }

        return references;
    }

    private async Task<byte[]?> BytesAsync(string storageKey, CancellationToken ct)
    {
        await using var stream = await objectStore.OpenReadAsync(storageKey, ct);
        if (stream is null)
        {
            return null;
        }

        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes, ct);
        return bytes.ToArray();
    }

    private async Task DeleteAsync(string storageKey)
    {
        try
        {
            await objectStore.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Couldn't delete the replaced view picture {StorageKey}.", storageKey);
        }
    }

    private static string Transcript(List<ImageInput> references, string prompt)
    {
        var text = new StringBuilder();
        foreach (var reference in references)
        {
            text.AppendLine($"[Reference picture] {reference.Caption}");
        }

        if (references.Count > 0)
        {
            text.AppendLine();
        }

        return text.Append(prompt).ToString();
    }

    private static string Extension(string contentType) => contentType switch
    {
        "image/jpeg" => "jpg",
        _ => contentType[(contentType.IndexOf('/') + 1)..],
    };
}
