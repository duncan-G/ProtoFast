using System.Text.RegularExpressions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Data.ThePlot;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;

namespace ProtoFast.Api.Services.OpenStories;

/// <summary>
/// Open stories are the <see cref="OpenLibrary"/>'s, so reading them ignores the per-user query
/// filters; the caller's copies are read and written under the caller's own.
/// </summary>
public sealed partial class OpenStoryService(
    ThePlotDbContext theplot,
    IUnitOfWorkFactory unitOfWorkFactory,
    IStoryRepository storyRepository) : global::ProtoFast.Api.OpenStories.OpenStoriesBase
{
    private const int MaxStories = 100;
    private const int MaxOpeningLength = 200;
    private const string OpenStoryNotFound = "That open story could not be found.";

    public override async Task<ListOpenStoriesReply> ListOpenStories(ListOpenStoriesRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListOpenStories));
        var stories = await theplot.Stories.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.UserId == OpenLibrary.OwnerId)
            .OrderByDescending(s => s.DateCreated)
            .ThenBy(s => s.Id)
            .Take(MaxStories)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.DateCreated,
                Scenes = s.Containers.SelectMany(c => c.Scenes).Count(),
                Characters = s.Characters.Count,
            })
            .ToListAsync(ct);
        var ids = stories.Select(s => s.Id).ToList();

        // The opening is the first action of each story's first scene.
        var actions = await theplot.SceneElements.IgnoreQueryFilters().AsNoTracking()
            .Where(e => ids.Contains(e.Scene.Container.StoryId) && e.Type == SceneElementTypeRecord.Action)
            .Where(e => !e.Scene.Container.Story.Containers.Any(c => c.Position < e.Scene.Container.Position))
            .Where(e => !e.Scene.Container.Scenes.Any(s => s.Position < e.Scene.Position))
            .Select(e => new { e.Scene.Container.StoryId, e.Position, e.Text })
            .ToListAsync(ct);
        var openings = actions
            .GroupBy(a => a.StoryId)
            .ToDictionary(g => g.Key, g => Opening(g.MinBy(a => a.Position)!.Text));

        var copies = (await theplot.Stories.AsNoTracking()
                .Where(s => s.SourceStoryId != null && ids.Contains(s.SourceStoryId.Value))
                .OrderByDescending(s => s.DateCreated)
                .Select(s => new { Source = s.SourceStoryId!.Value, s.Id })
                .ToListAsync(ct))
            .DistinctBy(c => c.Source)
            .ToDictionary(c => c.Source, c => c.Id);

        var reply = new ListOpenStoriesReply();
        reply.Stories.AddRange(stories.Select(s => new OpenStory
        {
            Id = s.Id.ToString(),
            Title = s.Title,
            Opening = openings.GetValueOrDefault(s.Id, ""),
            SceneCount = s.Scenes,
            CharacterCount = s.Characters,
            PublishedUnixSeconds = StoryMessages.UnixSeconds(s.DateCreated),
            ImportedStoryId = copies.TryGetValue(s.Id, out var copy) ? copy.ToString() : "",
        }));
        return reply;
    }

    public override async Task<ImportOpenStoryReply> ImportOpenStory(ImportOpenStoryRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var openId = StoryIds.Existing(request.OpenStoryId, OpenStoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(ImportOpenStory));

        var existing = await theplot.Stories.AsNoTracking()
            .Where(s => s.SourceStoryId == openId)
            .OrderByDescending(s => s.DateCreated)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return new ImportOpenStoryReply { Story = StoryMessages.ToSummary(existing), Created = false };
        }

        var open = await theplot.Stories.IgnoreQueryFilters().AsNoTracking().AsSplitQuery()
                       .Include(s => s.Characters)
                       .Include(s => s.Locations)
                       .Include(s => s.Props)
                       .Include(s => s.Containers).ThenInclude(c => c.Scenes).ThenInclude(s => s.Elements).ThenInclude(e => e.Mentions)
                       .FirstOrDefaultAsync(s => s.Id == openId && s.UserId == OpenLibrary.OwnerId, ct)
                   ?? throw StoryErrors.NotFound(OpenStoryNotFound);

        var copy = OpenStoryCopy.From(open);
        await storyRepository.AddAsync(copy, ct);
        await unitOfWork.CommitAsync(ct);
        return new ImportOpenStoryReply { Story = StoryMessages.ToSummary(copy), Created = true };
    }

    private static string Opening(string? text)
    {
        var line = Mentions().Replace(SuperPrefix().Replace(text ?? "", ""), "").Trim();
        return line.Length > MaxOpeningLength ? line[..MaxOpeningLength].TrimEnd() + "…" : line;
    }

    [GeneratedRegex(@"^\s*SUPER\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SuperPrefix();

    [GeneratedRegex(@"@(?=\S)")]
    private static partial Regex Mentions();
}
