using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Api.Admin;
using ProtoFast.Data.ThePlot;
using ProtoFast.Grpc;

namespace ProtoFast.Api.Services.Admin;

/// <summary>Counts span every user, so reads ignore the per-user query filters.</summary>
public sealed class AdminOverviewService(ThePlotDbContext theplot) : AdminOverview.AdminOverviewBase
{
    public override async Task<GetOverviewReply> GetOverview(GetOverviewRequest request, ServerCallContext context)
    {
        AdminAccess.Require(context, request.App);

        var reply = new GetOverviewReply();
        if (request.App == TheplotAdminService.App)
        {
            var ct = context.CancellationToken;
            var stories = theplot.Stories.IgnoreQueryFilters().AsNoTracking();
            reply.Metrics.Add(Metric("Writers", await stories.Select(s => s.UserId).Distinct().CountAsync(ct)));
            reply.Metrics.Add(Metric("Stories", await stories.CountAsync(ct)));
            reply.Metrics.Add(Metric("Scenes", await theplot.Scenes.IgnoreQueryFilters().CountAsync(ct)));
            reply.Metrics.Add(Metric("Documents", await theplot.Documents.IgnoreQueryFilters().CountAsync(ct)));
        }

        return reply;
    }

    private static OverviewMetric Metric(string label, long value) => new() { Label = label, Value = value };
}
