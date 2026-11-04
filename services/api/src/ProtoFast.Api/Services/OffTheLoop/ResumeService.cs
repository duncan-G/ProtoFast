using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using ApplicationRecord = ProtoFast.Data.OffTheLoop.Entities.Application;
using ResumeRecord = ProtoFast.Data.OffTheLoop.Entities.Resume;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>The caller's general resume and the ones tailored to their applications, one each.</summary>
public class ResumeService(
    IUnitOfWorkFactory<OffTheLoopDbContext> unitOfWorkFactory,
    IResumeRepository resumeRepository,
    IQueryFactory<ResumeRecord, IResumeQuery> resumeQueries,
    IApplicationRepository applicationRepository,
    IQueryFactory<ApplicationRecord, IApplicationQuery> applicationQueries) : Resumes.ResumesBase
{
    public override async Task<ListResumesReply> ListResumes(ListResumesRequest request, ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListResumes));
        var resumes = await resumeRepository.GetByQueryAsync(resumeQueries.Create().InOrder(), context.CancellationToken);

        var reply = new ListResumesReply();
        reply.Resumes.AddRange(resumes.Select(ResumeMessages.ToMessage));
        return reply;
    }

    public override async Task<SaveResumeReply> SaveResume(SaveResumeRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var changes = ResumeMessages.ToRecord(request.Resume ?? throw LoopErrors.Invalid("There’s no resume to save."));

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(SaveResume));
        // The query filter hides other users' applications, so theirs read as missing.
        if (changes.ApplicationId is { } applicationId
            && !await applicationRepository.ExistsByQueryAsync(applicationQueries.Create().WithId(applicationId), ct))
        {
            throw LoopErrors.Invalid(LoopErrors.ApplicationNotFound);
        }

        var resume = await resumeRepository.GetFirstByQueryAsync(resumeQueries.Create().ForApplication(changes.ApplicationId), ct);
        if (resume is null)
        {
            resume = changes;
            await resumeRepository.AddAsync(resume, ct);
        }
        else
        {
            ResumeMessages.Overwrite(resume, changes);
        }

        await unitOfWork.CommitAsync(ct);
        return new SaveResumeReply { Resume = ResumeMessages.ToMessage(resume) };
    }

    public override async Task<DeleteResumeReply> DeleteResume(DeleteResumeRequest request, ServerCallContext context)
    {
        var resumeId = LoopIds.Existing(request.ResumeId, LoopErrors.ResumeNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteResume));
        var deleted = await resumeRepository.DeleteByQueryAsync(resumeQueries.Create().WithId(resumeId), context.CancellationToken);
        if (deleted == 0)
        {
            throw LoopErrors.NotFound(LoopErrors.ResumeNotFound);
        }

        await unitOfWork.CommitAsync(context.CancellationToken);
        return new DeleteResumeReply();
    }
}
