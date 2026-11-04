using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using CompanyRecord = ProtoFast.Data.OffTheLoop.Entities.Company;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>The caller's companies; another user's ids read as not found.</summary>
public class CompanyService(
    IUnitOfWorkFactory<OffTheLoopDbContext> unitOfWorkFactory,
    ICompanyRepository companyRepository,
    IQueryFactory<CompanyRecord, ICompanyQuery> companyQueries) : Companies.CompaniesBase
{
    public override async Task<ListCompaniesReply> ListCompanies(ListCompaniesRequest request, ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListCompanies));
        var companies = await companyRepository.GetByQueryAsync(companyQueries.Create().ByName(), context.CancellationToken);

        var reply = new ListCompaniesReply();
        reply.Companies.AddRange(companies.Select(CompanyMessages.ToMessage));
        return reply;
    }

    public override async Task<CreateCompanyReply> CreateCompany(CreateCompanyRequest request, ServerCallContext context)
    {
        var company = CompanyMessages.ToRecord(request.Company ?? throw LoopErrors.Invalid("There’s no company to save."));

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateCompany));
        await companyRepository.AddAsync(company, context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new CreateCompanyReply { Company = CompanyMessages.ToMessage(company) };
    }

    public override async Task<UpdateCompanyReply> UpdateCompany(UpdateCompanyRequest request, ServerCallContext context)
    {
        var message = request.Company ?? throw LoopErrors.Invalid("There’s no company to save.");
        var companyId = LoopIds.Existing(message.Id, LoopErrors.CompanyNotFound);
        var changes = CompanyMessages.ToRecord(message);

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateCompany));
        var company = await companyRepository.GetFirstByQueryAsync(
                          companyQueries.Create().WithId(companyId), context.CancellationToken)
                      ?? throw LoopErrors.NotFound(LoopErrors.CompanyNotFound);
        CompanyMessages.Overwrite(company, changes);
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new UpdateCompanyReply { Company = CompanyMessages.ToMessage(company) };
    }

    public override async Task<DeleteCompanyReply> DeleteCompany(DeleteCompanyRequest request, ServerCallContext context)
    {
        var companyId = LoopIds.Existing(request.CompanyId, LoopErrors.CompanyNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteCompany));
        var deleted = await companyRepository.DeleteByQueryAsync(
            companyQueries.Create().WithId(companyId), context.CancellationToken);
        if (deleted == 0)
        {
            throw LoopErrors.NotFound(LoopErrors.CompanyNotFound);
        }

        await unitOfWork.CommitAsync(context.CancellationToken);
        return new DeleteCompanyReply();
    }
}
