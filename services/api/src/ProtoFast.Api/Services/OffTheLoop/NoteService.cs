using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using CompanyRecord = ProtoFast.Data.OffTheLoop.Entities.Company;
using NoteRecord = ProtoFast.Data.OffTheLoop.Entities.Note;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>The caller's notes; a company note belongs to one of the caller's companies.</summary>
public class NoteService(
    IUnitOfWorkFactory<OffTheLoopDbContext> unitOfWorkFactory,
    INoteRepository noteRepository,
    IQueryFactory<NoteRecord, INoteQuery> noteQueries,
    ICompanyRepository companyRepository,
    IQueryFactory<CompanyRecord, ICompanyQuery> companyQueries) : Notes.NotesBase
{
    private const int MaxImportedStories = 50;

    public override async Task<ListNotesReply> ListNotes(ListNotesRequest request, ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListNotes));
        var notes = await noteRepository.GetByQueryAsync(
            noteQueries.Create().RecentlyModifiedFirst(), context.CancellationToken);

        var reply = new ListNotesReply();
        reply.Notes.AddRange(notes.Select(NoteMessages.ToMessage));
        return reply;
    }

    public override async Task<CreateNoteReply> CreateNote(CreateNoteRequest request, ServerCallContext context)
    {
        var note = NoteMessages.ToRecord(request.Note ?? throw LoopErrors.Invalid("There’s no note to save."));

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateNote));
        await RequireCompanyAsync(note.CompanyId, context.CancellationToken);
        await noteRepository.AddAsync(note, context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new CreateNoteReply { Note = NoteMessages.ToMessage(note) };
    }

    public override async Task<UpdateNoteReply> UpdateNote(UpdateNoteRequest request, ServerCallContext context)
    {
        var message = request.Note ?? throw LoopErrors.Invalid("There’s no note to save.");
        var noteId = LoopIds.Existing(message.Id, LoopErrors.NoteNotFound);
        var changes = NoteMessages.ToRecord(message);

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateNote));
        var note = await noteRepository.GetFirstByQueryAsync(noteQueries.Create().WithId(noteId), context.CancellationToken)
                   ?? throw LoopErrors.NotFound(LoopErrors.NoteNotFound);
        await RequireCompanyAsync(changes.CompanyId, context.CancellationToken);
        NoteMessages.Overwrite(note, changes);
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new UpdateNoteReply { Note = NoteMessages.ToMessage(note) };
    }

    public override async Task<DeleteNoteReply> DeleteNote(DeleteNoteRequest request, ServerCallContext context)
    {
        var noteId = LoopIds.Existing(request.NoteId, LoopErrors.NoteNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteNote));
        var deleted = await noteRepository.DeleteByQueryAsync(noteQueries.Create().WithId(noteId), context.CancellationToken);
        if (deleted == 0)
        {
            throw LoopErrors.NotFound(LoopErrors.NoteNotFound);
        }

        await unitOfWork.CommitAsync(context.CancellationToken);
        return new DeleteNoteReply();
    }

    public override async Task<ImportStoriesReply> ImportStories(ImportStoriesRequest request, ServerCallContext context)
    {
        if (request.Stories.Count == 0)
        {
            throw LoopErrors.Invalid("There are no stories to import.");
        }

        if (request.Stories.Count > MaxImportedStories)
        {
            throw LoopErrors.Invalid($"Import at most {MaxImportedStories} stories at a time.");
        }

        var stories = request.Stories.Select((story, index) => ToStory(story, index + 1)).ToList();

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(ImportStories));
        foreach (var story in stories)
        {
            await noteRepository.AddAsync(story, context.CancellationToken);
        }

        await unitOfWork.CommitAsync(context.CancellationToken);

        var reply = new ImportStoriesReply();
        reply.Stories.AddRange(stories.Select(NoteMessages.ToMessage));
        return reply;
    }

    /// <summary>Names the story a problem is in, since one bad story stops the whole import.</summary>
    private static NoteRecord ToStory(Note message, int number)
    {
        if (message.Kind != NoteKind.Story)
        {
            throw LoopErrors.Invalid($"Story {number} isn’t a story; only stories can be imported.");
        }

        try
        {
            return NoteMessages.ToRecord(message);
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.InvalidArgument)
        {
            throw LoopErrors.Invalid($"Story {number}: {error.Status.Detail}");
        }
    }

    /// <summary>The query filter hides other users' companies, so theirs read as missing.</summary>
    private async Task RequireCompanyAsync(Guid? companyId, CancellationToken cancellationToken)
    {
        if (companyId is { } id
            && await companyRepository.GetFirstByQueryAsync(companyQueries.Create().WithId(id), cancellationToken) is null)
        {
            throw LoopErrors.Invalid(LoopErrors.CompanyNotFound);
        }
    }
}
