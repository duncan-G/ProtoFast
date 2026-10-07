using Grpc.Core;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.Grpc;
using Google.Protobuf;

namespace ProtoFast.Api.Services.Admin;

using EngineArtifactRef = ProtoFast.DocumentImport.Engine.Storage.ArtifactRef;

/// <summary>The engine's ledger for theplot's console; today every import is theplot's.</summary>
public sealed class TheplotRunsService(IRunInspector runs, IArtifactStore artifacts, IRegistry registry)
    : TheplotRuns.TheplotRunsBase
{
    public const string App = TheplotAdminService.App;

    private const int DefaultPageSize = 25;
    private const int DefaultTranscriptPage = 50;
    private const int DefaultArtifactBytes = 1 << 20;
    private const int MaxArtifactBytes = 4 << 20;
    private const string RunNotFound = "Run not found.";
    private const string ArtifactNotFound = "Artifact not found.";

    public override async Task<ListRunsReply> ListRuns(ListRunsRequest request, ServerCallContext context)
    {
        AdminAccess.Require(context, App);

        var page = await runs.ListAsync(
            new RunListQuery(
                string.IsNullOrEmpty(request.Family) ? null : request.Family,
                EngineMessages.ToRunMode(request.Mode),
                EngineMessages.ToRunStatus(request.Status),
                Math.Max(request.Page, 0),
                request.PageSize <= 0 ? DefaultPageSize : request.PageSize),
            context.CancellationToken);

        var reply = new ListRunsReply { Total = page.Total };
        reply.Runs.AddRange(page.Runs.Select(EngineMessages.From));
        return reply;
    }

    public override async Task<GetRunReply> GetRun(GetRunRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, RunNotFound);

        var detail = await runs.FindAsync(request.RunId, context.CancellationToken)
                     ?? throw new RpcException(new Status(StatusCode.NotFound, RunNotFound));

        var reply = new GetRunReply
        {
            Run = EngineMessages.From(detail.Header),
            MessageCount = detail.MessageCount,
            Progress = detail.Progress is null ? null : EngineMessages.From(detail.Progress, detail.Header.SourceId ?? ""),
        };
        reply.Stages.AddRange(detail.Stages.Select(EngineMessages.From));
        reply.Decisions.AddRange(detail.Decisions.Select(d => EngineMessages.From(d.Decision, d.RecordedAt)));
        return reply;
    }

    public override async Task<GetTranscriptReply> GetTranscript(GetTranscriptRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, RunNotFound);

        var page = await runs.TranscriptAsync(
            request.RunId,
            Math.Max(request.FromSequence, 0),
            request.Take <= 0 ? DefaultTranscriptPage : request.Take,
            context.CancellationToken);

        var reply = new GetTranscriptReply { Total = page.Total };
        reply.Messages.AddRange(page.Messages.Select(TranscriptMessages.From));
        reply.SystemPrompts.AddRange(page.SystemPrompts.Select(p => new SystemPrompt
        {
            FromSequence = p.FromSequence, Text = p.Prompt, RecordedUnixMs = EngineMessages.Millis(p.RecordedAt),
        }));
        return reply;
    }

    public override async Task<GetArtifactReply> GetArtifact(GetArtifactRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, ArtifactNotFound);

        // Keys are escaped per segment, so no value can leave runs/; a malformed hash is just absent.
        var reference = request.Artifact is { } a && !string.IsNullOrEmpty(a.RunId) && !string.IsNullOrEmpty(a.StageId) && IsHash(a.Hash)
            ? new EngineArtifactRef(a.RunId, a.StageId, a.Hash)
            : throw new RpcException(new Status(StatusCode.NotFound, ArtifactNotFound));
        var limit = request.MaxBytes <= 0 ? DefaultArtifactBytes : Math.Min(request.MaxBytes, MaxArtifactBytes);

        try
        {
            var contract = await artifacts.ContractOfAsync(reference, context.CancellationToken);
            await using var content = await artifacts.GetAsync(reference, context.CancellationToken);
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, context.CancellationToken);

            var bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
            return new GetArtifactReply
            {
                Contract = EngineMessages.From(contract),
                Size = buffer.Length,
                Content = ByteString.CopyFrom(bytes.Span[..Math.Min(bytes.Length, limit)]),
                Truncated = bytes.Length > limit,
            };
        }
        catch (KeyNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ArtifactNotFound));
        }
    }

    public override async Task<GetExecutorReply> GetExecutor(GetExecutorRequest request, ServerCallContext context)
    {
        AdminAccess.Require(context, App);

        try
        {
            var spec = await registry.ResolveAsync(new ExecutorRef(request.Id, request.Version), context.CancellationToken);
            var playbook = spec.Playbook is { } reference
                ? await registry.ResolveAsync(reference, context.CancellationToken)
                : null;
            return EngineMessages.From(spec, playbook);
        }
        catch (KeyNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Executor not found."));
        }
    }

    public override async Task<GetSkillReply> GetSkill(GetSkillRequest request, ServerCallContext context)
    {
        AdminAccess.Require(context, App);

        Skill skill;
        try
        {
            skill = await registry.ResolveAsync(new SkillRef(request.Id, request.Version), context.CancellationToken);
        }
        catch (KeyNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Skill not found."));
        }

        var reply = new GetSkillReply
        {
            Id = skill.Ref.Id,
            Version = skill.Ref.Version,
            Description = skill.Description,
            Instructions = skill.Instructions,
        };
        foreach (var script in skill.Scripts)
        {
            reply.Scripts.Add(new SkillScriptSource
            {
                Name = script.Name,
                Description = script.Description,
                CodeHash = script.CodeHash,
                Source = await SourceAsync(script.CodeHash, context.CancellationToken),
            });
        }

        return reply;
    }

    // A script whose source is gone still lists; the console shows it has none.
    private async Task<string> SourceAsync(string hash, CancellationToken ct)
    {
        try
        {
            await using var code = await registry.OpenCodeAsync(hash, ct);
            using var reader = new StreamReader(code);
            return await reader.ReadToEndAsync(ct);
        }
        catch (KeyNotFoundException)
        {
            return "";
        }
    }

    private static bool IsHash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);
}
