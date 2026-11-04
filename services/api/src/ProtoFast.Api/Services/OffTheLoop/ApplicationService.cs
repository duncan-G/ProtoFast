using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using ApplicationRecord = ProtoFast.Data.OffTheLoop.Entities.Application;
using CompanyRecord = ProtoFast.Data.OffTheLoop.Entities.Company;
using DocumentKindRecord = ProtoFast.Data.OffTheLoop.Entities.DocumentKind;
using DocumentRecord = ProtoFast.Data.OffTheLoop.Entities.Document;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>The caller's applications, each to one of the caller's companies; another user's ids read as not found.</summary>
public class ApplicationService(
    IUnitOfWorkFactory<OffTheLoopDbContext> unitOfWorkFactory,
    IApplicationRepository applicationRepository,
    IQueryFactory<ApplicationRecord, IApplicationQuery> applicationQueries,
    ICompanyRepository companyRepository,
    IQueryFactory<CompanyRecord, ICompanyQuery> companyQueries,
    IDocumentRepository documentRepository,
    IQueryFactory<DocumentRecord, IDocumentQuery> documentQueries) : Applications.ApplicationsBase
{
    public override async Task<ListApplicationsReply> ListApplications(
        ListApplicationsRequest request, ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListApplications));
        var applications = await applicationRepository.GetByQueryAsync(
            applicationQueries.Create().WithCompany().InLoopOrder(), context.CancellationToken);

        var reply = new ListApplicationsReply();
        reply.Applications.AddRange(applications.Select(ApplicationMessages.ToMessage));
        return reply;
    }

    public override async Task<CreateApplicationReply> CreateApplication(
        CreateApplicationRequest request, ServerCallContext context)
    {
        var application = ApplicationMessages.ToRecord(
            request.Application ?? throw LoopErrors.Invalid("There’s no application to save."));

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateApplication));
        application.Company = await RequireCompanyAsync(application.CompanyId, context.CancellationToken);
        await RequireDocumentsAsync(application, context.CancellationToken);
        await applicationRepository.AddAsync(application, context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new CreateApplicationReply { Application = ApplicationMessages.ToMessage(application) };
    }

    public override async Task<UpdateApplicationReply> UpdateApplication(
        UpdateApplicationRequest request, ServerCallContext context)
    {
        var message = request.Application ?? throw LoopErrors.Invalid("There’s no application to save.");
        var applicationId = LoopIds.Existing(message.Id, LoopErrors.ApplicationNotFound);
        var changes = ApplicationMessages.ToRecord(message);

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateApplication));
        var application = await applicationRepository.GetFirstByQueryAsync(
                              applicationQueries.Create().WithId(applicationId), context.CancellationToken)
                          ?? throw LoopErrors.NotFound(LoopErrors.ApplicationNotFound);
        var company = await RequireCompanyAsync(changes.CompanyId, context.CancellationToken);
        await RequireDocumentsAsync(changes, context.CancellationToken);
        ApplicationMessages.Overwrite(application, changes);
        application.Company = company;
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new UpdateApplicationReply { Application = ApplicationMessages.ToMessage(application) };
    }

    public override async Task<DeleteApplicationReply> DeleteApplication(
        DeleteApplicationRequest request, ServerCallContext context)
    {
        var applicationId = LoopIds.Existing(request.ApplicationId, LoopErrors.ApplicationNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteApplication));
        var deleted = await applicationRepository.DeleteByQueryAsync(
            applicationQueries.Create().WithId(applicationId), context.CancellationToken);
        if (deleted == 0)
        {
            throw LoopErrors.NotFound(LoopErrors.ApplicationNotFound);
        }

        await unitOfWork.CommitAsync(context.CancellationToken);
        return new DeleteApplicationReply();
    }

    /// <summary>The query filter hides other users' companies, so theirs read as missing.</summary>
    private async Task<CompanyRecord> RequireCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        await companyRepository.GetFirstByQueryAsync(companyQueries.Create().WithId(companyId), cancellationToken)
        ?? throw LoopErrors.Invalid(LoopErrors.CompanyNotFound);

    private async Task RequireDocumentsAsync(ApplicationRecord application, CancellationToken cancellationToken)
    {
        await RequireDocumentAsync(application.ResumeId, DocumentKindRecord.Resume, LoopErrors.ResumeNotFound, cancellationToken);
        await RequireDocumentAsync(application.CvId, DocumentKindRecord.Cv, LoopErrors.CvNotFound, cancellationToken);
    }

    /// <summary>Only the caller's own uploaded files of the slot's kind go with an application.</summary>
    private async Task RequireDocumentAsync(
        Guid? documentId, DocumentKindRecord kind, string notFound, CancellationToken cancellationToken)
    {
        if (documentId is not { } id)
        {
            return;
        }

        var document = await documentRepository.GetFirstByQueryAsync(
            documentQueries.Create().WithId(id).Uploaded(), cancellationToken);
        if (document?.Kind != kind)
        {
            throw LoopErrors.Invalid(notFound);
        }
    }
}
