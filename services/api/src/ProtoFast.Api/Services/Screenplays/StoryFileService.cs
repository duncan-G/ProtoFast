using Google.Protobuf;
using Grpc.Core;
using ProtoFast.Api.Services.Screenplays.Files;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Screenplays;

public class StoryFileService(
    IUnitOfWorkFactory unitOfWorkFactory,
    IStoryRepository storyRepository,
    IQueryFactory<StoryRecord, IStoryQuery> storyQueries,
    ISceneElementRepository sceneElementRepository,
    IQueryFactory<SceneElementRecord, ISceneElementQuery> sceneElementQueries) : StoryFiles.StoryFilesBase
{
    /// <summary>
    /// Rows are read before the library, so a row can only point at an entry deleted in between,
    /// which the export drops as the delete would have.
    /// </summary>
    public override async Task<ExportStoryReply> ExportStory(ExportStoryRequest request, ServerCallContext context)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ExportStory));
        var elements = await sceneElementRepository.GetByQueryAsync(
            sceneElementQueries.Create().InStory(storyId).WithMentions().InOrder(), context.CancellationToken);
        var story = await storyRepository.GetFirstByQueryAsync(
                        storyQueries.Create().WithId(storyId).WithOutline().WithLibrary(),
                        context.CancellationToken)
                    ?? throw StoryErrors.NotFound(StoryErrors.StoryNotFound);

        var file = StoryExport.ToFile(story, elements.ToLookup(e => e.SceneId));
        return new ExportStoryReply
        {
            FileName = TheplotJson.FileName(story.Title),
            Content = UnsafeByteOperations.UnsafeWrap(TheplotJson.Write(file)),
        };
    }

    public override async Task<ImportStoryReply> ImportStory(ImportStoryRequest request, ServerCallContext context)
    {
        var story = StoryImport.ToStory(TheplotJson.Read(request.Content.Memory));
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(ImportStory));
        await storyRepository.AddAsync(story, context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);
        return new ImportStoryReply { Story = StoryMessages.ToSummary(story) };
    }
}
