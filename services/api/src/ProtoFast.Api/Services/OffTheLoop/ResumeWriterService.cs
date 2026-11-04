using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using ApplicationRecord = ProtoFast.Data.OffTheLoop.Entities.Application;
using DocumentRecord = ProtoFast.Data.OffTheLoop.Entities.Document;
using NoteKindRecord = ProtoFast.Data.OffTheLoop.Entities.NoteKind;
using NoteRecord = ProtoFast.Data.OffTheLoop.Entities.Note;
using ResumeRecord = ProtoFast.Data.OffTheLoop.Entities.Resume;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>Streams a model's reading of a resume file, or its tailoring of the general resume; the client saves what the caller keeps.</summary>
public class ResumeWriterService(
    IUnitOfWorkFactory<OffTheLoopDbContext> unitOfWorkFactory,
    IDocumentRepository documentRepository,
    IQueryFactory<DocumentRecord, IDocumentQuery> documentQueries,
    IResumeRepository resumeRepository,
    IQueryFactory<ResumeRecord, IResumeQuery> resumeQueries,
    IApplicationRepository applicationRepository,
    IQueryFactory<ApplicationRecord, IApplicationQuery> applicationQueries,
    INoteRepository noteRepository,
    IQueryFactory<NoteRecord, INoteQuery> noteQueries,
    ResumeFiles files,
    IResearchModel model,
    TimeProvider time,
    ILogger<ResumeWriterService> logger) : ResumeWriter.ResumeWriterBase
{
    /// <summary>Trims the instructions in place, or throws what the call would.</summary>
    public static void CheckTailoring(TailorResumeRequest request) =>
        request.Instructions = LoopText.Optional(request.Instructions, WriterService.MaxInstruction, "What to lean on");

    public override async Task ReadResume(
        ReadResumeRequest request, IServerStreamWriter<ReadResumeReply> responseStream, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        DocumentRecord document;
        using (unitOfWorkFactory.CreateReadOnly(nameof(ReadResume)))
        {
            document = await FindDocumentAsync(documentRepository, documentQueries, request.DocumentId, ct);
        }

        var file = await files.ReadAsync(document, ct);
        await RunAsync(
            ResumeReading.Brief(file, document.FileName, Today),
            step => responseStream.WriteAsync(new ReadResumeReply { Step = step }, ct),
            findings => responseStream.WriteAsync(
                new ReadResumeReply { Findings = new ResumeFindings { Resume = ResumeReading.ToResume(findings.Answer) } }, ct),
            ct);
    }

    public override async Task TailorResume(
        TailorResumeRequest request, IServerStreamWriter<TailorResumeReply> responseStream, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        CheckTailoring(request);
        ApplicationRecord application;
        ResumeRecord general;
        List<NoteRecord> stories;
        using (unitOfWorkFactory.CreateReadOnly(nameof(TailorResume)))
        {
            var applicationId = LoopIds.Existing(request.ApplicationId, LoopErrors.ApplicationNotFound);
            application = await applicationRepository.GetFirstByQueryAsync(
                              applicationQueries.Create().WithId(applicationId).WithCompany(), ct)
                          ?? throw LoopErrors.NotFound(LoopErrors.ApplicationNotFound);
            general = await FindGeneralAsync(resumeRepository, resumeQueries, ct);
            stories = (await noteRepository.GetByQueryAsync(
                noteQueries.Create().OfKind(NoteKindRecord.Story).RecentlyModifiedFirst(), ct)).ToList();
        }

        await RunAsync(
            ResumeTailoring.Brief(general, stories, application, request.Instructions, Today),
            step => responseStream.WriteAsync(new TailorResumeReply { Step = step }, ct),
            findings => responseStream.WriteAsync(
                new TailorResumeReply { Findings = new ResumeFindings { Resume = ResumeTailoring.ToResume(findings.Answer, general) } }, ct),
            ct);
    }

    /// <summary>Uploaded, whatever kind it was sent as.</summary>
    public static async Task<DocumentRecord> FindDocumentAsync(
        IDocumentRepository documents, IQueryFactory<DocumentRecord, IDocumentQuery> queries, string id, CancellationToken ct)
    {
        var documentId = LoopIds.Existing(id, LoopErrors.DocumentNotFound);
        return await documents.GetFirstByQueryAsync(queries.Create().WithId(documentId).Uploaded(), ct)
               ?? throw LoopErrors.NotFound(LoopErrors.DocumentNotFound);
    }

    /// <summary>Tailoring needs something to tailor.</summary>
    public static async Task<ResumeRecord> FindGeneralAsync(
        IResumeRepository resumes, IQueryFactory<ResumeRecord, IResumeQuery> queries, CancellationToken ct)
    {
        var general = await resumes.GetFirstByQueryAsync(queries.Create().ForApplication(null), ct);
        return general is not null && !ResumeMessages.IsEmpty(general)
            ? general
            : throw new RpcException(new Status(StatusCode.FailedPrecondition, LoopErrors.ResumeEmpty));
    }

    private DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    /// <summary>The research model's own failures are worded for research; these are about a resume.</summary>
    private async Task RunAsync(
        ResearchBrief brief, Func<ResearchStep, Task> onStep, Func<ResearchFindings, Task> onFindings, CancellationToken ct)
    {
        try
        {
            await foreach (var update in model.ResearchAsync(brief, ct))
            {
                switch (update)
                {
                    case ResearchProgress progress:
                        await onStep(new ResearchStep { Action = progress.Action, Subject = progress.Subject });
                        break;
                    case ResearchFindings findings:
                        await onFindings(findings);
                        break;
                }
            }
        }
        catch (RpcException e) when (e.Status.Detail is LoopErrors.ResearchOff or LoopErrors.ResearchRefused)
        {
            var message = e.Status.Detail == LoopErrors.ResearchOff ? LoopErrors.ResumeAiOff : LoopErrors.ResumeRefused;
            throw new RpcException(new Status(e.StatusCode, message));
        }
        catch (Exception e) when (e is not RpcException && !ct.IsCancellationRequested)
        {
            logger.LogError(e, "The resume model failed");
            throw new RpcException(new Status(StatusCode.Unavailable, LoopErrors.ResumeFailed));
        }
    }
}
