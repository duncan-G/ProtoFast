using System.Diagnostics;
using Google.Protobuf;
using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using ApplicationRecord = ProtoFast.Data.OffTheLoop.Entities.Application;
using CompanyRecord = ProtoFast.Data.OffTheLoop.Entities.Company;
using DocumentRecord = ProtoFast.Data.OffTheLoop.Entities.Document;
using NoteRecord = ProtoFast.Data.OffTheLoop.Entities.Note;
using ResumeRecord = ProtoFast.Data.OffTheLoop.Entities.Resume;

namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>
/// The caller's side of the run queue. Work is checked as its call would check it before it is
/// queued, so a run fails only on what the model does.
/// </summary>
public sealed class RunsService(
    IUnitOfWorkFactory<OffTheLoopDbContext> unitOfWorkFactory,
    IModelRunRepository runRepository,
    IQueryFactory<ModelRun, IModelRunQuery> runQueries,
    ICompanyRepository companyRepository,
    IQueryFactory<CompanyRecord, ICompanyQuery> companyQueries,
    IApplicationRepository applicationRepository,
    IQueryFactory<ApplicationRecord, IApplicationQuery> applicationQueries,
    INoteRepository noteRepository,
    IQueryFactory<NoteRecord, INoteQuery> noteQueries,
    IDocumentRepository documentRepository,
    IQueryFactory<DocumentRecord, IDocumentQuery> documentQueries,
    IResumeRepository resumeRepository,
    IQueryFactory<ResumeRecord, IResumeQuery> resumeQueries,
    UserContext userContext,
    RunStore store,
    RunSignal signal,
    RunOptions options) : Runs.RunsBase
{
    public override async Task<StartRunReply> StartRun(StartRunRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var work = request.Work ?? throw LoopErrors.Invalid(LoopErrors.RunEmpty);
        var run = new ModelRun
        {
            Id = Guid.CreateVersion7(),
            Label = "",
            Work = [],
            Status = ModelRunStatus.Queued,
            TraceParent = Activity.Current?.Id,
        };

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(StartRun));
        switch (work.WorkCase)
        {
            case RunWork.WorkOneofCase.ResearchCompany:
                run.Kind = ModelRunKind.ResearchCompany;
                run.CompanyId = await CompanyAsync(work.ResearchCompany.CompanyId, ct);
                break;
            case RunWork.WorkOneofCase.ReadPosting:
                ResearcherService.CheckPosting(work.ReadPosting);
                run.Kind = ModelRunKind.ReadPosting;
                run.CompanyId = work.ReadPosting.CompanyId.Length > 0 ? await CompanyAsync(work.ReadPosting.CompanyId, ct) : null;
                run.ApplicationId = work.ReadPosting.ApplicationId.Length > 0
                    ? await ApplicationAsync(work.ReadPosting.ApplicationId, run.CompanyId, ct)
                    : null;
                break;
            case RunWork.WorkOneofCase.Write:
                WriterService.Check(work.Write);
                run.Kind = ModelRunKind.Write;
                (run.NoteId, run.NoteModifiedAt) = await NoteAsync(work.Write.NoteId, ct);
                break;
            case RunWork.WorkOneofCase.ReadResume:
                run.Kind = ModelRunKind.ReadResume;
                await ResumeWriterService.FindDocumentAsync(documentRepository, documentQueries, work.ReadResume.DocumentId, ct);
                break;
            case RunWork.WorkOneofCase.TailorResume:
                ResumeWriterService.CheckTailoring(work.TailorResume);
                run.Kind = ModelRunKind.TailorResume;
                (run.ApplicationId, run.CompanyId) = await TailoredAsync(work.TailorResume.ApplicationId, ct);
                await ResumeWriterService.FindGeneralAsync(resumeRepository, resumeQueries, ct);
                break;
            default:
                throw LoopErrors.Invalid(LoopErrors.RunEmpty);
        }

        run.Label = LabelOf(request.Label, run.Kind);
        run.Work = work.ToByteArray();
        await runRepository.DeleteByQueryAsync(
            runQueries.Create().OnSubject(run.Kind, run.CompanyId, run.ApplicationId, run.NoteId), ct);
        await runRepository.AddAsync(run, ct);
        await unitOfWork.CommitAsync(ct);

        signal.Wake();
        return new StartRunReply { Run = RunMessages.ToMessage(run) };
    }

    public override async Task<ListRunsReply> ListRuns(ListRunsRequest request, ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListRuns));
        var runs = await runRepository.GetByQueryAsync(runQueries.Create().InOrder(), r => new ModelRun
        {
            Id = r.Id,
            Kind = r.Kind,
            Label = r.Label,
            Work = Array.Empty<byte>(),
            Status = r.Status,
            Error = r.Error,
            CompanyId = r.CompanyId,
            ApplicationId = r.ApplicationId,
            NoteId = r.NoteId,
            Attempts = r.Attempts,
            StartedAt = r.StartedAt,
            FinishedAt = r.FinishedAt,
            DateCreated = r.DateCreated,
        }, context.CancellationToken);
        var reply = new ListRunsReply();
        reply.Runs.AddRange(runs.Select(RunMessages.ToMessage));
        return reply;
    }

    public override async Task<GetRunReply> GetRun(GetRunRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var id = LoopIds.Existing(request.RunId, LoopErrors.RunNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(GetRun));
        var run = await runRepository.GetFirstByQueryAsync(runQueries.Create().WithId(id), ct)
                  ?? throw LoopErrors.NotFound(LoopErrors.RunNotFound);
        var reply = new GetRunReply
        {
            Run = RunMessages.ToMessage(run),
            Work = RunWork.Parser.ParseFrom(run.Work),
            Progress = run.Progress is { } progress ? RunProgress.Parser.ParseFrom(progress) : new RunProgress(),
            Result = run.Result is { } result ? RunResult.Parser.ParseFrom(result) : null,
        };
        if (run.NoteId is { } noteId)
        {
            var modified = await noteRepository.GetFirstByQueryAsync(
                noteQueries.Create().WithId(noteId), n => (DateTime?)n.DateLastModified, ct);
            reply.NoteChanged = modified != run.NoteModifiedAt;
        }

        return reply;
    }

    public override async Task WatchRun(
        WatchRunRequest request, IServerStreamWriter<WatchRunReply> responseStream, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var id = LoopIds.Existing(request.RunId, LoopErrors.RunNotFound);
        var userId = userContext.CurrentUserId ?? throw LoopErrors.NotFound(LoopErrors.RunNotFound);
        var attempt = request.Attempt;
        var steps = Math.Max(request.FromStep, 0);
        var text = Math.Max(request.FromText, 0);
        var watching = Stopwatch.StartNew();
        var version = -1;
        Run? sent = null;
        while (true)
        {
            var watched = await store.WatchAsync(id, userId, version, ct);
            if (watched is null)
            {
                // Gone after the first read: dismissed while it was watched.
                if (sent is null)
                {
                    throw LoopErrors.NotFound(LoopErrors.RunNotFound);
                }

                return;
            }

            var finished = RunMessages.IsFinished(watched.Run.Status);
            if (watched.Changed)
            {
                version = watched.Run.Version;
                var reply = new WatchRunReply();
                // A writing run keeps its text when taken over, so only research starts over.
                if (attempt != 0 && watched.Run.Attempts != attempt && watched.Run.Kind != ModelRunKind.Write)
                {
                    reply.Restarted = true;
                    steps = 0;
                    text = 0;
                }

                attempt = watched.Run.Attempts;
                var message = RunMessages.ToMessage(watched.Run);
                if (!message.Equals(sent))
                {
                    reply.Run = sent = message;
                }

                var progress = watched.Progress;
                reply.Progress = new RunProgress { Text = text < progress.Text.Length ? progress.Text[text..] : "" };
                reply.Progress.Steps.AddRange(progress.Steps.Skip(steps));
                steps = Math.Max(steps, progress.Steps.Count);
                text = Math.Max(text, progress.Text.Length);
                if (finished)
                {
                    reply.Result = watched.Result;
                }

                if (reply.Run is not null || reply.Restarted || reply.Progress.Steps.Count > 0
                    || reply.Progress.Text.Length > 0 || reply.Result is not null)
                {
                    await responseStream.WriteAsync(reply, ct);
                }
            }

            if (finished || watching.Elapsed >= options.WatchFor)
            {
                return;
            }

            await Task.Delay(options.WatchEvery, ct);
        }
    }

    public override async Task<CancelRunReply> CancelRun(CancelRunRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var id = LoopIds.Existing(request.RunId, LoopErrors.RunNotFound);
        await store.CancelAsync(id, userContext.CurrentUserId ?? "", ct);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(CancelRun));
        var run = await runRepository.GetFirstByQueryAsync(runQueries.Create().WithId(id), ct)
                  ?? throw LoopErrors.NotFound(LoopErrors.RunNotFound);
        return new CancelRunReply { Run = RunMessages.ToMessage(run) };
    }

    public override async Task<DismissRunsReply> DismissRuns(DismissRunsRequest request, ServerCallContext context)
    {
        var ids = request.RunIds.Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty).ToList();
        if (ids.Count == 0)
        {
            return new DismissRunsReply();
        }

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DismissRuns));
        await runRepository.DeleteByQueryAsync(runQueries.Create().WithIds(ids), context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);
        return new DismissRunsReply();
    }

    private static string LabelOf(string label, ModelRunKind kind)
    {
        label = label.Trim();
        if (label.Length == 0)
        {
            return kind switch
            {
                ModelRunKind.ResearchCompany => "Research",
                ModelRunKind.ReadPosting => "Read a posting",
                ModelRunKind.ReadResume => "Read a resume",
                ModelRunKind.TailorResume => "Tailor a resume",
                _ => "Write",
            };
        }

        return label[..Math.Min(label.Length, OffTheLoopLimits.Line)];
    }

    private async Task<Guid> CompanyAsync(string id, CancellationToken ct)
    {
        var companyId = LoopIds.Existing(id, LoopErrors.CompanyNotFound);
        return await companyRepository.ExistsByQueryAsync(companyQueries.Create().WithId(companyId), ct)
            ? companyId
            : throw LoopErrors.NotFound(LoopErrors.CompanyNotFound);
    }

    private async Task<Guid> ApplicationAsync(string id, Guid? companyId, CancellationToken ct)
    {
        var applicationId = LoopIds.Existing(id, LoopErrors.ApplicationNotFound);
        var company = await applicationRepository.GetFirstByQueryAsync(
            applicationQueries.Create().WithId(applicationId), a => (Guid?)a.CompanyId, ct);
        return company is not null && company == companyId
            ? applicationId
            : throw LoopErrors.NotFound(LoopErrors.ApplicationNotFound);
    }

    private async Task<(Guid, Guid)> TailoredAsync(string id, CancellationToken ct)
    {
        var applicationId = LoopIds.Existing(id, LoopErrors.ApplicationNotFound);
        var company = await applicationRepository.GetFirstByQueryAsync(
            applicationQueries.Create().WithId(applicationId), a => (Guid?)a.CompanyId, ct);
        return company is { } companyId ? (applicationId, companyId) : throw LoopErrors.NotFound(LoopErrors.ApplicationNotFound);
    }

    private async Task<(Guid, DateTime)> NoteAsync(string id, CancellationToken ct)
    {
        if (id.Length == 0)
        {
            throw LoopErrors.Invalid(LoopErrors.RunNoteMissing);
        }

        var noteId = LoopIds.Existing(id, LoopErrors.NoteNotFound);
        var modified = await noteRepository.GetFirstByQueryAsync(
            noteQueries.Create().WithId(noteId), n => (DateTime?)n.DateLastModified, ct);
        return modified is { } at ? (noteId, at) : throw LoopErrors.NotFound(LoopErrors.NoteNotFound);
    }
}
