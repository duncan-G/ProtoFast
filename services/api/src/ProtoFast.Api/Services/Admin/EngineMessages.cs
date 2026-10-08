using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Families;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using Proto = ProtoFast.Api.Admin.Theplot;

namespace ProtoFast.Api.Services.Admin;

/// <summary>The one place that knows the engine's record shapes; a ledger change touches this file.</summary>
internal static class EngineMessages
{
    public static Proto.RunHeader From(RunHeader header)
    {
        var (family, generation) = DocumentFamilyKeys.Split(header.DocumentSignature.Family);
        var message = new Proto.RunHeader
        {
            RunId = header.RunId,
            Family = family,
            Generation = generation,
            Mode = From(header.Mode),
            Status = header.Status switch
            {
                RunStatus.Open => Proto.RunStatus.Open,
                RunStatus.Closed => Proto.RunStatus.Closed,
                RunStatus.Abandoned => Proto.RunStatus.Abandoned,
                _ => Proto.RunStatus.Unspecified,
            },
            OpenedUnixMs = Millis(header.OpenedAt),
            ClosedUnixMs = Millis(header.ClosedAt),
            AbandonedUnixMs = Millis(header.AbandonedAt),
            Failure = header.Failure ?? "",
            TraceId = header.Trace?.Id ?? "",
            StageAttempts = header.StageAttempts,
            Passed = header.Passed,
            CostUsdMicros = Micros(header.Cost),
            SourceId = header.SourceId ?? "",
        };
        foreach (var (key, value) in header.DocumentSignature.Facets)
        {
            message.Facets[key] = value;
        }

        return message;
    }

    public static Proto.StageAttempt From(RecordedStage stage)
    {
        var record = stage.Record;
        var message = new Proto.StageAttempt
        {
            StageId = record.StageId,
            InputContract = From(record.Stage.Input),
            OutputContract = From(record.Stage.Output),
            ExecutorId = record.Executor.Id,
            ExecutorVersion = record.Executor.Version,
            Tier = From(record.Tier),
            Shadow = record.IsShadow,
            Output = record.Output.IsNone ? null : From(record.Output),
            TraceId = record.Result.Trace?.Id ?? "",
            Spend = From(record.Result.Cost),
            Passed = record.Passed,
            Degraded = record.Degraded,
            RecordedUnixMs = Millis(stage.RecordedAt),
        };
        message.DependsOn.AddRange(record.Stage.DependsOn);
        message.VerifierIds.AddRange(record.Stage.Verifiers);
        message.Inputs.AddRange(record.Inputs.Select(From));
        message.Decisions.AddRange(record.Result.Decisions.Select(d => From(d, null)));
        message.Verdicts.AddRange(record.Verdicts.Select(From));
        return message;
    }

    public static Proto.DecisionRecord From(Decision decision, DateTimeOffset? recordedAt) =>
        new()
        {
            Key = decision.Key,
            Choice = decision.Choice,
            Rationale = decision.Rationale,
            Confidence = decision.Confidence,
            RecordedUnixMs = Millis(recordedAt),
        };

    public static Proto.VerifierVerdict From(VerifierResult result)
    {
        var message = new Proto.VerifierVerdict
        {
            VerifierId = result.VerifierId,
            Verdict = result.Verdict switch
            {
                Verdict.Pass => Proto.Verdict.Pass,
                Verdict.Degraded => Proto.Verdict.Degraded,
                Verdict.Fail => Proto.Verdict.Fail,
                _ => Proto.Verdict.Unspecified,
            },
            Reason = result.Reason,
        };
        message.Findings.AddRange(result.Findings.Select(f => new Proto.VerdictFinding { Path = f.Path, Message = f.Message }));
        return message;
    }

    public static Proto.SourceProgress From(RunProgress progress, string sourceId) =>
        new()
        {
            SourceId = sourceId,
            Phase = progress.Phase switch
            {
                RunPhase.Preparing => Proto.ProgressPhase.Preparing,
                RunPhase.Running => Proto.ProgressPhase.Running,
                RunPhase.Finishing => Proto.ProgressPhase.Finishing,
                RunPhase.Finished => Proto.ProgressPhase.Finished,
                RunPhase.Retrying => Proto.ProgressPhase.Retrying,
                RunPhase.Failed => Proto.ProgressPhase.Failed,
                RunPhase.Cancelled => Proto.ProgressPhase.Cancelled,
                _ => Proto.ProgressPhase.Unspecified,
            },
            StageId = progress.StageId ?? "",
            Message = progress.Message ?? "",
            ResultId = progress.ResultId ?? "",
            CostUsdMicros = Micros(progress.Cost),
        };

    public static Proto.GetExecutorReply From(ExecutorSpec spec, Playbook? playbook)
    {
        var message = new Proto.GetExecutorReply
        {
            Id = spec.Ref.Id,
            Version = spec.Ref.Version,
            Tier = From(spec.Tier),
            ModelClass = spec.ModelClass ?? "",
            CodeAssembly = spec.CodeAssembly ?? "",
            Origin = spec.Origin switch
            {
                ExecutorOrigin.Seed => Proto.ExecutorOrigin.Seed,
                ExecutorOrigin.AgentDefined => Proto.ExecutorOrigin.AgentDefined,
                ExecutorOrigin.Distilled => Proto.ExecutorOrigin.Distilled,
                _ => Proto.ExecutorOrigin.Unspecified,
            },
            Promoted = spec.Promoted,
        };
        message.Tools.AddRange(spec.Tools);
        if (playbook is not null)
        {
            message.Playbook = new Proto.ExecutorPlaybook
            {
                Id = playbook.Ref.Id,
                Version = playbook.Ref.Version,
                Instructions = playbook.Instructions,
            };
            message.Playbook.Examples.AddRange(playbook.Examples.Select(e => new Proto.PlaybookExample
            {
                Input = From(e.Input),
                Output = From(e.Output),
            }));
            foreach (var (key, rule) in playbook.Rules)
            {
                message.Playbook.Rules[key] = rule;
            }
        }

        return message;
    }

    public static Proto.FamilySummary From(DocumentFamilySummary summary) =>
        new()
        {
            Family = summary.Family,
            Info = summary.Info is null ? null : From(summary.Info),
            Generation = summary.Generation,
            Mode = From(summary.Mode),
            WorkflowId = summary.Workflow?.Id ?? "",
            WorkflowVersion = summary.Workflow?.Version ?? 0,
            Runs = summary.Runs,
            OpenRuns = summary.OpenRuns,
            LastRunUnixMs = Millis(summary.LastRunAt),
            Skills = summary.Skills,
            Executors = summary.Executors,
            Verifiers = summary.Verifiers,
        };

    public static Proto.GetFamilyReply From(DocumentFamilyDetail detail)
    {
        var message = new Proto.GetFamilyReply
        {
            Family = detail.Family,
            Info = detail.Info is null ? null : From(detail.Info),
            CurrentGeneration = detail.CurrentGeneration,
            ResetUnixMs = Millis(detail.ResetAt),
            Generation = detail.Generation,
            Mode = From(detail.Policy.Mode),
            WorkflowId = detail.Policy.Workflow?.Id ?? "",
            WorkflowVersion = detail.Policy.Workflow?.Version ?? 0,
            ConfidenceMean = detail.Policy.Confidence.Mean,
            ConfidenceObservations = detail.Policy.Confidence.Observations,
        };
        message.Skills.AddRange(detail.Skills.Select(s => new Proto.SkillEntry
        {
            Id = s.Ref.Id, Version = s.Ref.Version, AddedUnixMs = Millis(s.AddedAt),
            RemovedUnixMs = Millis(s.RemovedAt), RemovalReason = s.RemovalReason ?? "",
        }));
        message.Executors.AddRange(detail.Executors.Select(e => new Proto.ExecutorEntry
        {
            Id = e.Ref.Id, Version = e.Ref.Version, AddedUnixMs = Millis(e.AddedAt),
        }));
        message.Verifiers.AddRange(detail.Verifiers.Select(v => new Proto.VerifierEntry
        {
            Id = v.Spec.Id, StageId = v.Spec.StageId, Rubric = v.Spec.Rubric, AddedUnixMs = Millis(v.AddedAt),
        }));
        message.StagePolicies.AddRange(detail.StagePolicies.Select(From));
        message.MinedWorkflows.AddRange(detail.MinedWorkflows.Select(w => new Proto.MinedWorkflowSummary
        {
            Id = w.Ref.Id, Version = w.Ref.Version, Stages = w.Stages, MinedUnixMs = Millis(w.MinedAt), Promoted = w.Promoted,
        }));
        message.RunsByGeneration.AddRange(detail.RunsByGeneration.Select(g => new Proto.GenerationRunCounts
        {
            Generation = g.Generation, Runs = g.Runs, OpenRuns = g.OpenRuns, LastRunUnixMs = Millis(g.LastRunAt),
        }));
        return message;
    }

    public static Proto.StagePolicy From(PolicyRow row)
    {
        var message = new Proto.StagePolicy
        {
            StageId = row.StageId,
            Primary = From(row.Primary),
            ConfidenceMean = row.Confidence.Mean,
            ConfidenceObservations = row.Confidence.Observations,
            ShadowMean = row.ShadowConfidence.Mean,
            ShadowObservations = row.ShadowConfidence.Observations,
            UpdatedUnixMs = Millis(row.UpdatedAt),
        };
        if (row.Shadow is { } shadow)
        {
            message.Shadow = From(shadow);
        }

        message.Ladder.AddRange(row.Ladder
            .OrderBy(rung => rung.Key)
            .Select(rung => new Proto.LadderRung
            {
                Tier = From(rung.Key), ExecutorId = rung.Value.Id, ExecutorVersion = rung.Value.Version,
            }));
        return message;
    }

    public static Proto.ArtifactRef From(ArtifactRef reference) =>
        new() { RunId = reference.RunId, StageId = reference.StageId, Hash = reference.Hash };

    public static Proto.ContractRef From(ContractRef contract) =>
        new() { SchemaId = contract.SchemaId, Version = contract.Version };

    public static Proto.Spend From(Cost cost) =>
        new() { UsdMicros = Micros(cost.Amount), DurationMs = (long)cost.Duration.TotalMilliseconds };

    public static Proto.RunMode From(RunMode mode) => mode switch
    {
        RunMode.Discovery => Proto.RunMode.Discovery,
        RunMode.Scheduled => Proto.RunMode.Scheduled,
        _ => Proto.RunMode.Unspecified,
    };

    public static RunMode? ToRunMode(Proto.RunMode mode) => mode switch
    {
        Proto.RunMode.Discovery => RunMode.Discovery,
        Proto.RunMode.Scheduled => RunMode.Scheduled,
        _ => null,
    };

    public static RunStatus? ToRunStatus(Proto.RunStatus status) => status switch
    {
        Proto.RunStatus.Open => RunStatus.Open,
        Proto.RunStatus.Closed => RunStatus.Closed,
        Proto.RunStatus.Abandoned => RunStatus.Abandoned,
        _ => null,
    };

    public static Proto.ExecutorTier From(Tier tier) => tier switch
    {
        Tier.Orchestrator => Proto.ExecutorTier.Orchestrator,
        Tier.DelegateLarge => Proto.ExecutorTier.DelegateLarge,
        Tier.DelegateMedium => Proto.ExecutorTier.DelegateMedium,
        Tier.DelegateSmall => Proto.ExecutorTier.DelegateSmall,
        Tier.Codified => Proto.ExecutorTier.Codified,
        _ => Proto.ExecutorTier.Unspecified,
    };

    public static long Millis(DateTimeOffset? at) => at?.ToUnixTimeMilliseconds() ?? 0;

    public static long Micros(decimal usd) => (long)Math.Round(usd * 1_000_000m);

    private static Proto.FamilyInfo From(DocumentFamilyInfo info) =>
        new()
        {
            DisplayName = info.DisplayName,
            Description = info.Description,
            CreatedBy = info.CreatedBy ?? "",
            CreatedUnixMs = Millis(info.CreatedAt),
            UpdatedUnixMs = Millis(info.UpdatedAt),
        };
}
