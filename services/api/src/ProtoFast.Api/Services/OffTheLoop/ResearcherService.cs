using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using CompanyRecord = ProtoFast.Data.OffTheLoop.Entities.Company;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>Streams a model's web research into the company page; the client saves what the caller keeps.</summary>
public class ResearcherService(
    IUnitOfWorkFactory<OffTheLoopDbContext> unitOfWorkFactory,
    ICompanyRepository companyRepository,
    IQueryFactory<CompanyRecord, ICompanyQuery> companyQueries,
    IResearchModel model,
    PostingPages pages,
    TimeProvider time,
    ILogger<ResearcherService> logger) : Researcher.ResearcherBase
{
    public override async Task ResearchCompany(
        ResearchCompanyRequest request, IServerStreamWriter<ResearchCompanyReply> responseStream, ServerCallContext context)
    {
        var company = await FindCompanyAsync(request.CompanyId, context.CancellationToken);
        await RunAsync(
            CompanyResearch.Brief(company, Today),
            step => responseStream.WriteAsync(new ResearchCompanyReply { Step = step }, context.CancellationToken),
            findings => responseStream.WriteAsync(
                new ResearchCompanyReply { Findings = CompanyResearch.ToFindings(findings.Answer, findings.Sources) },
                context.CancellationToken),
            context.CancellationToken);
    }

    /// <summary>Trims the link and text in place, or throws what the call would.</summary>
    public static void CheckPosting(ReadPostingRequest request)
    {
        request.Url = LoopText.Url(request.Url, OffTheLoopLimits.Url, "The posting link");
        request.Text = LoopText.Optional(request.Text, OffTheLoopLimits.Posting, "The posting");
        if (request.Url.Length == 0 && request.Text.Length == 0)
        {
            throw LoopErrors.Invalid(LoopErrors.PostingMissing);
        }
    }

    public override async Task ReadPosting(
        ReadPostingRequest request, IServerStreamWriter<ReadPostingReply> responseStream, ServerCallContext context)
    {
        CheckPosting(request);
        var url = request.Url;
        var text = request.Text;
        var company = request.CompanyId.Length > 0
            ? await FindCompanyAsync(request.CompanyId, context.CancellationToken)
            : null;
        await RunAsync(
            PostingResearch.Brief(url, text, company, Today),
            step => responseStream.WriteAsync(new ReadPostingReply { Step = step }, context.CancellationToken),
            async findings =>
            {
                var posting = text.Length > 0
                    ? text
                    : await pages.ReadAsync(PostingResearch.Pages(findings.Answer, url), context.CancellationToken);
                await responseStream.WriteAsync(
                    new ReadPostingReply { Findings = PostingResearch.ToFindings(findings.Answer, url, posting, findings.Sources) },
                    context.CancellationToken);
            },
            context.CancellationToken);
    }

    private DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    /// <summary>Read before the model runs, so no connection is held while it does.</summary>
    private async Task<CompanyRecord> FindCompanyAsync(string id, CancellationToken cancellationToken)
    {
        var companyId = LoopIds.Existing(id, LoopErrors.CompanyNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(FindCompanyAsync));
        return await companyRepository.GetFirstByQueryAsync(companyQueries.Create().WithId(companyId), cancellationToken)
               ?? throw LoopErrors.NotFound(LoopErrors.CompanyNotFound);
    }

    private async Task RunAsync(
        ResearchBrief brief, Func<ResearchStep, Task> onStep, Func<ResearchFindings, Task> onFindings,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in model.ResearchAsync(brief, cancellationToken))
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
        catch (Exception e) when (e is not RpcException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(e, "The research model failed");
            throw new RpcException(new Status(StatusCode.Unavailable, LoopErrors.ResearchFailed));
        }
    }
}
