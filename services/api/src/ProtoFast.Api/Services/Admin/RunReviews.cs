using ProtoFast.DocumentImport.Engine.Briefing;
using Proto = ProtoFast.Api.Admin.Theplot;

namespace ProtoFast.Api.Services.Admin;

public static class RunReviews
{
    public static Proto.RunBrief From(RunBriefing briefing)
    {
        var reply = new Proto.RunBrief
        {
            Status = briefing.Status switch
            {
                RunBriefStatus.Briefing => Proto.BriefStatus.Briefing,
                RunBriefStatus.Briefed => Proto.BriefStatus.Briefed,
                RunBriefStatus.Failed => Proto.BriefStatus.Failed,
                _ => Proto.BriefStatus.Unspecified,
            },
            Attempts = briefing.Attempts,
            Error = briefing.Error ?? "",
            UpdatedUnixMs = EngineMessages.Millis(briefing.UpdatedAt),
        };

        if (briefing.Brief is { } brief)
        {
            reply.Outcome = brief.Outcome;
            reply.Overview = brief.Overview;
            reply.ModelId = brief.ModelId;
            reply.CostUsdMicros = EngineMessages.Micros(brief.Cost);
            reply.Flags.AddRange(brief.Flags.Select(f => new Proto.BriefFlag
            {
                Kind = f.Kind, Detail = f.Detail, Sequence = f.Sequence ?? 0,
            }));
        }

        return reply;
    }

    public static Proto.RunStep From(RunStep step, RunBrief? brief)
    {
        var reply = new Proto.RunStep
        {
            Sequence = step.Sequence,
            Headline = step.Headline,
            Brief = brief?.Steps.FirstOrDefault(s => s.Sequence == step.Sequence)?.Text ?? "",
        };
        reply.Calls.AddRange(step.Calls.Select(call =>
        {
            var proto = new Proto.StepCall
            {
                CallId = call.CallId,
                Tool = call.Tool,
                Skill = call.Skill ?? "",
                Script = call.Script ?? "",
                IsError = call.IsError,
                Headline = call.Headline,
            };
            proto.Effects.AddRange(call.Effects.Select(e => new Proto.StepEffect
            {
                Kind = e.Kind.ToString(), Summary = e.Summary, Depth = e.Depth, Subject = e.Subject ?? "",
            }));
            return proto;
        }));
        return reply;
    }
}
