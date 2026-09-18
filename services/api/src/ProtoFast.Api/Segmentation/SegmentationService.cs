using System.Text.Json;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Storage;

// The generated stubs land in this same namespace (csharp_namespace = "ProtoFast.Api"), and
// several proto messages share a name with the domain type they carry — Run, RunEvent, Paragraph,
// SectionNode, Finding. Aliasing the domain side keeps every line below unambiguous about which
// of the two it means, which matters most in the mapping methods where both appear at once.
using CoreFormats = ProtoFast.Segmentation.Core.Ingest.SourceFormats;
using CoreModel = ProtoFast.Segmentation.Core.Model;
using DbReviewTask = ProtoFast.Segmentation.Data.Entities.ReviewTask;
using DbRun = ProtoFast.Segmentation.Data.Entities.Run;
using DbRunEvent = ProtoFast.Segmentation.Data.Entities.RunEvent;
using DbRunPhase = ProtoFast.Segmentation.Data.Entities.RunPhase;
using DbUpload = ProtoFast.Segmentation.Data.Entities.Upload;

namespace ProtoFast.Api;

/// <summary>
/// The gRPC surface of plan §17.
///
/// <para>Three rules shape everything here. Ownership comes from the internal JWT's subject and
/// never from a request field. <c>api</c> never calls a provider and never reads an artifact's
/// contents — it presigns, enqueues, and reads Postgres. And large documents never cross gRPC:
/// they go browser → S3 directly, and the request carries only an upload id.</para>
/// </summary>
public sealed class SegmentationService(
    SegmentationDbContext db,
    IPresignedUrlFactory urls,
    IArtifactStore artifacts,
    IRunQueue queue,
    IOptions<SegmentationApiOptions> options,
    IOptions<StorageOptions> storage,
    ILogger<SegmentationService> logger) : Segmentation.SegmentationBase
{
    private readonly SegmentationApiOptions _options = options.Value;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Mints the presigned POST the browser uploads through (ingest plan §7, §14).
    ///
    /// <para>Two things are validated before anything is signed, and both end up <em>in</em> the
    /// signature. The format has to be on the allowlist, and the canonical extension that comes
    /// back from it — not the caller's filename — is what the key is built from. The size cap
    /// becomes the policy's <c>content-length-range</c>, so a client that ignores it is refused by
    /// S3 rather than believed.</para>
    /// </summary>
    public override async Task<CreateUploadReply> CreateUpload(
        CreateUploadRequest request, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);

        if (!CoreFormats.TryResolve(request.FileName, request.ContentType, out var format))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"ThePlot cannot read {CoreFormats.DescribeRejected(request.FileName)} files yet."));
        }

        if (request.SizeBytes <= 0 || request.SizeBytes > _options.MaxUploadBytes)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"The document must be between 1 byte and {_options.MaxUploadBytes / (1024 * 1024)} MB."));
        }

        var uploadId = CoreModel.Ids.NewRunId();

        // The key the policy pins. The extension is the allowlist's, so a filename of
        // "../../../etc/passwd" cannot reach outside this caller's own prefix.
        var sourceKey = ArtifactKeys.UploadSource(caller.Subject, uploadId, format.Extension);

        var post = urls.PresignPost(sourceKey, format.MediaType, _options.MaxUploadBytes);

        // Recorded before the URL is handed out, so SubmitRun can check that an upload belongs to
        // its caller without trusting a key from the request, and phase 0 can rebuild the source
        // key without re-parsing a filename.
        db.Uploads.Add(new DbUpload
        {
            UploadId = uploadId,
            OwnerSubject = caller.Subject,
            FileName = request.FileName,
            SizeBytes = request.SizeBytes,
            MediaType = format.MediaType,
            SourceExtension = format.Extension,
            RequiresConversion = format.RequiresConversion,
            WithLayout = format.ProducesLayout,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = post.ExpiresAt,
        });

        await db.SaveChangesAsync(context.CancellationToken);

        var reply = new CreateUploadReply
        {
            UploadId = uploadId,
            PostUrl = post.PostUrl,
            MaxBytes = post.MaxBytes,
            ExpiresUnixSeconds = post.ExpiresAt.ToUnixTimeSeconds(),
        };

        // Posted back verbatim, with the file part last: S3 stops reading at the file, so a field
        // after it is never seen and the upload fails the policy it was signed against.
        foreach (var (name, value) in post.Fields)
        {
            reply.Fields.Add(name, value);
        }

        return reply;
    }

    /// <summary>
    /// The allowlist and the cap, served from the same table <c>CreateUpload</c> validates against
    /// (ingest plan C9) — so the upload page's <c>accept</c> attribute cannot drift from what the
    /// server will actually sign for.
    /// </summary>
    public override Task<ListSourceFormatsReply> ListSourceFormats(
        ListSourceFormatsRequest request, ServerCallContext context)
    {
        var reply = new ListSourceFormatsReply { MaxBytes = _options.MaxUploadBytes };

        reply.Formats.AddRange(CoreFormats.All.Select(f => new SourceFormat
        {
            Extension = f.Extension,
            MediaType = f.MediaType,
            Label = f.Label,
            ProducesLayout = f.ProducesLayout,
            OcrCapable = f.OcrCapable,
        }));

        return Task.FromResult(reply);
    }

    public override async Task<SubmitRunReply> SubmitRun(SubmitRunRequest request, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "An idempotency_key is required: without one a retried submission is a second run and a second bill."));
        }

        // Idempotency first. A retry has to return the original run before anything else happens.
        var existing = await db.Runs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.OwnerSubject == caller.Subject && r.IdempotencyKey == request.IdempotencyKey,
                context.CancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "SubmitRun replayed idempotency key for run {RunId}; not enqueuing again.", existing.RunId);
            return new SubmitRunReply { RunId = existing.RunId };
        }

        var upload = await db.Uploads
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UploadId == request.UploadId, context.CancellationToken);

        // A missing upload and an upload belonging to somebody else get the same answer, so the
        // RPC cannot be used to probe which upload ids exist.
        if (upload is null || upload.OwnerSubject != caller.Subject)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "That upload does not exist."));
        }

        // The SOURCE key, not the Markdown one. For a PDF the Markdown genuinely does not exist
        // yet — the converter writes it at phase 0 — so checking the Markdown here would fail
        // every non-Markdown submission with "has not finished uploading".
        var sourceKey = ArtifactKeys.UploadSource(
            caller.Subject, upload.UploadId, SourceExtensionOf(upload));

        if (!await artifacts.ExistsAsync(sourceKey, context.CancellationToken))
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "The document has not finished uploading. Post it to the presigned URL first."));
        }

        var augmentations = ValidateAugmentations(request.Augmentations);

        var run = new DbRun
        {
            RunId = CoreModel.Ids.NewRunId(),
            OwnerSubject = caller.Subject,
            DocumentId = string.IsNullOrWhiteSpace(request.DocumentId) ? upload.FileName : request.DocumentId,
            DocumentFamily = string.IsNullOrWhiteSpace(request.FamilyHint)
                ? ProtoFast.Segmentation.Core.Ingest.FamilyDetector.Unknown
                : request.FamilyHint,
            Sensitivity = ToSensitivity(request.Sensitivity),
            Priority = request.Priority == Priority.Bulk ? CoreModel.RunPriority.Bulk : CoreModel.RunPriority.Realtime,
            UploadId = upload.UploadId,
            IdempotencyKey = request.IdempotencyKey,
            Augmentations = string.Join(',', augmentations),
            RequiresReview = request.RequireReview,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Runs.Add(run);

        // Seed the phase rows so ThePlot can draw the whole ladder immediately rather than having
        // rows appear one at a time as the worker reaches them.
        foreach (var phase in Enum.GetValues<CoreModel.PipelinePhase>())
        {
            db.RunPhases.Add(new DbRunPhase { RunId = run.RunId, Phase = phase, State = CoreModel.PhaseState.Pending });
        }

        db.RunEvents.Add(new DbRunEvent
        {
            RunId = run.RunId,
            Phase = CoreModel.PipelinePhase.Ingest,
            State = CoreModel.PhaseState.Pending,
            Message = "submitted",
            At = DateTimeOffset.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two submissions with the same key raced past the check above. The unique index on
            // (owner_subject, idempotency_key) is the real arbiter; whoever lost returns the
            // winner's run rather than an error, because from the caller's side the submission
            // did succeed.
            db.ChangeTracker.Clear();

            var winner = await IsIdempotencyConflictAsync(
                caller.Subject, request.IdempotencyKey, context.CancellationToken);

            if (winner is null)
            {
                throw;
            }

            return new SubmitRunReply { RunId = winner };
        }

        await queue.SendRunAsync(new RunMessage(run.RunId, run.Priority), context.CancellationToken);

        logger.LogInformation(
            "Run {RunId} submitted by {Subject} ({Priority}, {Sensitivity})",
            run.RunId, caller.Subject, run.Priority, run.Sensitivity);

        return new SubmitRunReply { RunId = run.RunId };
    }

    public override async Task<Run> GetRun(GetRunRequest request, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);
        var run = await LoadOwnedRunAsync(caller, request.RunId, context.CancellationToken);
        return ToProto(run);
    }

    /// <summary>
    /// Tails <c>run_events</c> (plan §17).
    ///
    /// <para>It polls rather than listening, because the events are written by a different process
    /// on a different host and Postgres <c>LISTEN/NOTIFY</c> would need a dedicated connection per
    /// watcher. The poll is an index scan on <c>(run_id, id)</c> for rows after the last one sent,
    /// which costs almost nothing, and the stream ends as soon as the run reaches a terminal
    /// state so a watcher never lingers.</para>
    /// </summary>
    public override async Task WatchRun(
        GetRunRequest request, IServerStreamWriter<RunEvent> responseStream, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);
        await LoadOwnedRunAsync(caller, request.RunId, context.CancellationToken);

        long lastSeen = 0;

        while (!context.CancellationToken.IsCancellationRequested)
        {
            var events = await db.RunEvents
                .AsNoTracking()
                .Where(e => e.RunId == request.RunId && e.Id > lastSeen)
                .OrderBy(e => e.Id)
                .Take(200)
                .ToListAsync(context.CancellationToken);

            foreach (var evt in events)
            {
                await responseStream.WriteAsync(new RunEvent
                {
                    RunId = evt.RunId,
                    AtUnixSeconds = evt.At.ToUnixTimeSeconds(),
                    Phase = evt.Phase.ToString(),
                    State = ToProto(evt.State),
                    Message = evt.Message,
                    Sequence = evt.Id,
                }, context.CancellationToken);

                lastSeen = evt.Id;
            }

            var finished = await db.Runs
                .AsNoTracking()
                .Where(r => r.RunId == request.RunId)
                .Select(r => r.PublishedAt != null || r.Cancelled || r.Error != null)
                .FirstOrDefaultAsync(context.CancellationToken);

            if (finished && events.Count == 0)
            {
                return;
            }

            if (events.Count == 0)
            {
                await Task.Delay(_options.WatchPollInterval, context.CancellationToken);
            }
        }
    }

    public override async Task<Result> GetResult(GetRunRequest request, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);
        await LoadOwnedRunAsync(caller, request.RunId, context.CancellationToken);

        var result = await db.RunResults
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RunId == request.RunId, context.CancellationToken);

        if (result is null)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition, "This run has not published a result yet."));
        }

        var root = JsonSerializer.Deserialize<CoreModel.SectionNode>(result.TreeJson, Json);
        var paragraphs = JsonSerializer.Deserialize<List<CoreModel.Paragraph>>(result.ParagraphsJson, Json) ?? [];
        var augmentations = JsonSerializer.Deserialize<List<AugmentationJson>>(result.AugmentationsJson, Json) ?? [];

        var reply = new Result
        {
            RunId = result.RunId,
            Root = root is null ? new SectionNode() : ToProto(root),
            TreeHash = result.TreeHash,
            FrozenUnixSeconds = result.FrozenAt.ToUnixTimeSeconds(),
        };

        reply.Paragraphs.AddRange(paragraphs.Select(p => new Paragraph
        {
            ParagraphId = p.ParagraphId,
            Text = p.Text,
            WordCount = p.WordCount,
            Kind = p.Kind.ToString().ToLowerInvariant(),
            ContentHash = p.ContentHash,
        }));

        reply.Augmentations.AddRange(augmentations.Select(a => new Augmentation
        {
            ParagraphId = a.ParagraphId,
            Type = a.Type,
            Json = a.Json,
            ReviewVerdict = a.ReviewVerdict,
        }));

        return reply;
    }

    public override async Task<GetArtifactReply> GetArtifact(GetArtifactRequest request, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);
        await LoadOwnedRunAsync(caller, request.RunId, context.CancellationToken);

        // The key is client-supplied, so it is checked against the run's own prefix. Without this
        // a caller who owns any run could presign any object in the bucket, including another
        // user's document (plan §24.1).
        if (!ArtifactKeys.BelongsToRun(request.ArtifactKey, request.RunId))
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied, "That artifact key does not belong to this run."));
        }

        if (!await artifacts.ExistsAsync(request.ArtifactKey, context.CancellationToken))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "That artifact does not exist."));
        }

        var expires = DateTimeOffset.UtcNow.Add(storage.Value.DownloadUrlTtl);

        return new GetArtifactReply
        {
            GetUrl = urls.PresignGet(request.ArtifactKey),
            ExpiresUnixSeconds = expires.ToUnixTimeSeconds(),
        };
    }

    public override async Task<Run> CancelRun(GetRunRequest request, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);
        var run = await LoadOwnedRunAsync(caller, request.RunId, context.CancellationToken, tracking: true);

        run.Cancelled = true;
        run.UpdatedAt = DateTimeOffset.UtcNow;

        db.RunEvents.Add(new DbRunEvent
        {
            RunId = run.RunId,
            Phase = CoreModel.PipelinePhase.Ingest,
            State = CoreModel.PhaseState.Failed,
            Message = "cancelled by the owner",
            At = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(context.CancellationToken);

        // The worker observes this at its next superstep (plan §17), so the run stops between
        // phases rather than being killed inside a provider call it has already paid for.
        return ToProto(run);
    }

    public override async Task<SubmitRunReply> RerunFrom(RerunFromRequest request, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);
        var run = await LoadOwnedRunAsync(caller, request.RunId, context.CancellationToken, tracking: true);

        if (!Enum.IsDefined(typeof(CoreModel.PipelinePhase), request.FromPhase))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument, $"'{request.FromPhase}' is not a pipeline phase."));
        }

        var fromPhase = (CoreModel.PipelinePhase)request.FromPhase;

        // Reset the phases at and after the requested one, so the ladder in ThePlot shows the
        // re-run rather than the previous attempt's completed state.
        //
        // Loaded and mutated rather than ExecuteUpdate: LoadOwnedRunAsync already tracked these
        // rows through its Include, and a set-based update would leave those tracked copies stale
        // — the ToProto below would then report the state before the reset. Thirteen rows at most.
        foreach (var phase in run.Phases.Where(p => p.Phase >= fromPhase))
        {
            phase.State = CoreModel.PhaseState.Pending;
            phase.RepairRounds = 0;
            phase.Error = null;
            phase.StartedAt = null;
            phase.FinishedAt = null;
            phase.ArtifactKey = null;
        }

        run.Cancelled = false;
        run.Error = null;
        run.UpdatedAt = DateTimeOffset.UtcNow;

        db.RunEvents.Add(new DbRunEvent
        {
            RunId = run.RunId,
            Phase = fromPhase,
            State = CoreModel.PhaseState.Pending,
            Message = $"re-queued from phase {(int)fromPhase} ({fromPhase})",
            At = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(context.CancellationToken);

        await queue.SendRunAsync(
            new RunMessage(run.RunId, run.Priority) { FromPhase = (int)fromPhase },
            context.CancellationToken);

        return new SubmitRunReply { RunId = run.RunId };
    }

    public override async Task<ListReviewsReply> ListReviews(ListReviewsRequest request, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);
        var isReviewer = caller.HasRole(_options.ReviewerRole);

        var status = string.IsNullOrWhiteSpace(request.Status) ? "pending" : request.Status;
        var pageSize = request.PageSize is > 0 and <= 200 ? request.PageSize : 50;

        var query = db.ReviewTasks.AsNoTracking().Where(r => r.Status == status);

        // A reviewer sees the queue; everybody else sees only their own documents. That is what
        // lets the review screen be a page of ThePlot rather than a separate client.
        if (!isReviewer)
        {
            query = query.Where(r => r.OwnerSubject == caller.Subject);
        }

        if (!string.IsNullOrWhiteSpace(request.PageToken))
        {
            query = query.Where(r => string.Compare(r.ReviewId, request.PageToken) > 0);
        }

        var tasks = await query
            .OrderBy(r => r.ReviewId)
            .Take(pageSize + 1)
            .ToListAsync(context.CancellationToken);

        var reply = new ListReviewsReply();
        foreach (var task in tasks.Take(pageSize))
        {
            var item = new ReviewTask
            {
                ReviewId = task.ReviewId,
                RunId = task.RunId,
                DocumentId = task.DocumentId,
                Family = task.DocumentFamily,
                CreatedUnixSeconds = task.CreatedAt.ToUnixTimeSeconds(),
            };

            item.Findings.AddRange(
                (JsonSerializer.Deserialize<List<CoreModel.Finding>>(task.FindingsJson, Json) ?? [])
                .Select(f => new Finding
                {
                    Severity = f.Severity.ToString().ToLowerInvariant(),
                    Type = f.Type,
                    Message = f.Message,
                }));

            reply.Reviews.Add(item);
        }

        if (tasks.Count > pageSize)
        {
            reply.NextPageToken = tasks[pageSize - 1].ReviewId;
        }

        return reply;
    }

    public override async Task<Run> SubmitReviewDecision(
        SubmitReviewDecisionRequest request, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);

        var task = await db.ReviewTasks
            .FirstOrDefaultAsync(r => r.ReviewId == request.ReviewId, context.CancellationToken);

        if (task is null || (task.OwnerSubject != caller.Subject && !caller.HasRole(_options.ReviewerRole)))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "That review does not exist."));
        }

        if (task.Status == "complete")
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition, "That review has already been decided."));
        }

        var decision = request.Decision switch
        {
            "approve" => CoreModel.ReviewDecisionKind.Approve,
            "approve_with_edits" => CoreModel.ReviewDecisionKind.ApproveWithEdits,
            "reject" => CoreModel.ReviewDecisionKind.Reject,
            _ => throw new RpcException(new Status(
                StatusCode.InvalidArgument, "decision must be approve, approve_with_edits or reject")),
        };

        task.Status = "complete";
        task.Decision = decision;
        task.Notes = request.Notes;
        task.DecidedBy = caller.Subject;
        task.DecidedAt = DateTimeOffset.UtcNow;
        task.EditsJson = JsonSerializer.Serialize(
            request.Edits.Select(e => new CoreModel.ParagraphEdit(
                e.Op, e.ParagraphId,
                string.IsNullOrEmpty(e.WithParagraphId) ? null : e.WithParagraphId,
                e.BeforeSentence == 0 ? null : e.BeforeSentence,
                request.Notes ?? string.Empty)),
            Json);

        var run = await db.Runs.FirstOrDefaultAsync(r => r.RunId == task.RunId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "That review's run no longer exists."));

        run.ReviewState = decision == CoreModel.ReviewDecisionKind.Reject ? "rejected" : "approved";
        run.UpdatedAt = DateTimeOffset.UtcNow;

        db.RunEvents.Add(new DbRunEvent
        {
            RunId = run.RunId,
            Phase = CoreModel.PipelinePhase.HumanGate,
            State = CoreModel.PhaseState.Done,
            Message = $"{request.Decision} by {caller.Subject}",
            At = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(context.CancellationToken);

        // api never touches a workflow. It records the decision and re-queues the run; the worker
        // is what resumes the suspended workflow with it (plan §9.10).
        await queue.SendBatchPollAsync(
            new BatchPollMessage(run.RunId, task.ReviewId, "review", CoreModel.AgentRole.StructureReviewer, 0),
            TimeSpan.Zero,
            context.CancellationToken);

        return ToProto(run);
    }

    public override Task<ListModelsReply> ListModels(ListModelsRequest request, ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);
        caller.RequireRole(_options.AdminRole);

        // The registry and its live headroom live in the worker, which is the only component that
        // holds the routing stack (plan §17: "api never calls a provider"). Qualification is in
        // Postgres, so that half is answerable here; headroom is not, and is reported as unknown
        // rather than invented.
        var reply = new ListModelsReply();

        var qualified = db.Qualifications
            .AsNoTracking()
            .Where(q => q.Qualified)
            .ToList()
            .GroupBy(q => q.ModelKey, StringComparer.OrdinalIgnoreCase);

        foreach (var group in qualified)
        {
            reply.Models.Add(new ModelInfo
            {
                Key = group.Key,
                Provider = group.Key.Split('/', 2)[0],
                Tier = string.Empty,
                HeadroomFraction = -1,
                CircuitOpen = false,
                QualifiedRoles = { group.Select(q => q.Role.ToString()).Distinct() },
            });
        }

        return Task.FromResult(reply);
    }

    private async Task<DbRun> LoadOwnedRunAsync(
        CallerIdentity caller, string runId, CancellationToken ct, bool tracking = false)
    {
        var query = tracking ? db.Runs : db.Runs.AsNoTracking();

        // Ownership is part of the WHERE clause rather than a check after the fact: a run that is
        // not the caller's is not found, so the two cases are indistinguishable from outside.
        var run = await query
            .Include(r => r.Phases)
            .FirstOrDefaultAsync(r => r.RunId == runId && r.OwnerSubject == caller.Subject, ct);

        return run ?? throw new RpcException(new Status(StatusCode.NotFound, "That run does not exist."));
    }

    /// <summary>
    /// The upload's canonical extension, falling back to <c>.md</c> for rows written before the
    /// conversion feature — those were always Markdown, and their key really is the Markdown one.
    /// </summary>
    private static string SourceExtensionOf(DbUpload upload) =>
        string.IsNullOrEmpty(upload.SourceExtension) ? ".md" : upload.SourceExtension;

    private async Task<string?> IsIdempotencyConflictAsync(string subject, string key, CancellationToken ct) =>
        await db.Runs
            .AsNoTracking()
            .Where(r => r.OwnerSubject == subject && r.IdempotencyKey == key)
            .Select(r => r.RunId)
            .FirstOrDefaultAsync(ct);

    private IReadOnlyList<string> ValidateAugmentations(IEnumerable<string> requested)
    {
        var names = requested
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_options.AllowedAugmentations.Count == 0)
        {
            return names;
        }

        var unknown = names
            .Where(n => !_options.AllowedAugmentations.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (unknown.Count > 0)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"Unknown augmentation type(s): {string.Join(", ", unknown)}. "
                + $"Available: {string.Join(", ", _options.AllowedAugmentations)}."));
        }

        return names;
    }

    private static Run ToProto(DbRun run)
    {
        var proto = new Run
        {
            RunId = run.RunId,
            DocumentId = run.DocumentId,
            Family = run.DocumentFamily,
            Condition = run.Condition.ToString().ToLowerInvariant(),
            Sensitivity = ToProto(run.Sensitivity),
            Priority = run.Priority == CoreModel.RunPriority.Bulk ? Priority.Bulk : Priority.Realtime,
            RequiresReview = run.RequiresReview,
            ReviewState = run.ReviewState,
            CostUsd = (double)run.CostUsd,
            CreatedUnixSeconds = run.CreatedAt.ToUnixTimeSeconds(),
            Error = run.Error ?? string.Empty,
            Cancelled = run.Cancelled,
            TreeHash = run.TreeHash ?? string.Empty,
        };

        foreach (var (phase, model) in run.PinnedModels)
        {
            proto.PinnedModels.Add(phase, model);
        }

        proto.Phases.AddRange(run.Phases
            .OrderBy(p => p.Phase)
            .Select(p => new Phase
            {
                Index = (int)p.Phase,
                Name = p.Phase.ToString(),
                State = ToProto(p.State),
                ArtifactKey = p.ArtifactKey ?? string.Empty,
                StartedUnixSeconds = p.StartedAt?.ToUnixTimeSeconds() ?? 0,
                FinishedUnixSeconds = p.FinishedAt?.ToUnixTimeSeconds() ?? 0,
                Error = p.Error ?? string.Empty,
            }));

        return proto;
    }

    private static SectionNode ToProto(CoreModel.SectionNode node)
    {
        var proto = new SectionNode
        {
            SectionId = node.SectionId,
            Title = node.Title,
            TitleInferred = node.TitleInferred,
            Level = node.Level,
            HeadingLineId = node.HeadingLineId ?? string.Empty,
        };

        proto.ParagraphIds.AddRange(node.ParagraphIds);
        proto.Children.AddRange(node.Children.Select(ToProto));
        return proto;
    }

    private static CoreModel.Sensitivity ToSensitivity(Sensitivity sensitivity) => sensitivity switch
    {
        Sensitivity.Public => CoreModel.Sensitivity.Public,
        Sensitivity.Confidential => CoreModel.Sensitivity.Confidential,
        Sensitivity.Restricted => CoreModel.Sensitivity.Restricted,
        // Unspecified defaults to Internal rather than Public: a caller that did not say is not
        // asserting that the document may go anywhere.
        _ => CoreModel.Sensitivity.Internal,
    };

    private static Sensitivity ToProto(CoreModel.Sensitivity sensitivity) => sensitivity switch
    {
        CoreModel.Sensitivity.Public => Sensitivity.Public,
        CoreModel.Sensitivity.Confidential => Sensitivity.Confidential,
        CoreModel.Sensitivity.Restricted => Sensitivity.Restricted,
        _ => Sensitivity.Internal,
    };

    private static PhaseState ToProto(CoreModel.PhaseState state) => state switch
    {
        CoreModel.PhaseState.Running => PhaseState.Running,
        CoreModel.PhaseState.Done => PhaseState.Done,
        CoreModel.PhaseState.Failed => PhaseState.Failed,
        CoreModel.PhaseState.Skipped => PhaseState.Skipped,
        _ => PhaseState.Pending,
    };

    /// <summary>The augmentation shape stored in <c>run_results.augmentations_json</c>.</summary>
    private sealed record AugmentationJson(string ParagraphId, string Type, string Json, string ReviewVerdict);
}
