# Agent workflow engine

A policy-routed stage engine. The engine knows only stages, executors, verifiers and policy.
Everything about *what* a stage does lives in data: workflow definitions, executor specs, playbooks.

```
            ┌──────────── control plane ──────────────────────┐
 input ──▶ Classifier ──▶ Scheduler ──▶ Executors ──▶ Verifiers
                            │   (escalation)            │
                       PolicyStore (frozen per run)     │
            ┌──────────── data plane ────────────────────┼────┐
            │  ArtifactStore      RunLedger      Registry      │
            └────────────────────────────────┬────────────────┘
                                             ▼
            ┌──────────── learning plane ─────────────────────┐
            │  OutcomeQueue ──▶ PolicyUpdater ──▶ Distiller    │
            └─────────────────────────────────────────────────┘
```

Two run modes per document family. **Discovery**: the agent owns the loop and the engine records (section 6).
**Scheduled**: the engine owns the loop and the agent is one executor (section 5). Document families start in
Discovery and move to Scheduled as a workflow is mined from their ledgers.

Three roles, never shared within a run: whoever owns the loop owns routing and escalation,
executors own reasoning, verifiers own truth.

## 0. Leaf types

Every ref is a versioned, immutable identity. Anything resolved by ref can be cached forever.

```csharp
public readonly record struct ContractRef(string SchemaId, int Version);
public readonly record struct ArtifactRef(string RunId, string StageId, string Hash);
public readonly record struct ExecutorRef(string Id, int Version);
public readonly record struct PlaybookRef(string Id, int Version);
public readonly record struct WorkflowRef(string Id, int Version);
public readonly record struct TraceRef(string Id);

public sealed record Budget(decimal MaxCost, TimeSpan MaxDuration);
public sealed record Cost(decimal Amount, TimeSpan Duration);
public sealed record Finding(string Path, string Message);
public sealed record Example(ArtifactRef Input, ArtifactRef Output);
```

## 1. Core contracts

A workflow is a versioned DAG of stages. A stage is a contract, not an implementation.

```csharp
public sealed record WorkflowDefinition(WorkflowRef Ref, IReadOnlyList<StageDefinition> Stages);

public sealed record StageDefinition(
    string Id,
    IReadOnlyList<string> DependsOn,          // stage ids; empty = root, receives the run input
    ContractRef Input,                        // schema the input artifact must satisfy
    ContractRef Output,                       // schema the output artifact must satisfy
    IReadOnlyList<string> Verifiers,          // verifier ids; deterministic ones run first
    Budget Budget);
```

The engine sees one input artifact and one output artifact per stage. Fan-out over units of a
document is the executor's business, behind its contract. The scheduler never inspects content.

The classifier turns a run's input into a `Signature`. Its `Family` is the policy key: a document
family is a set of documents similar enough to share one workflow and policy, such as one vendor's
invoices. The taxonomy is data; the engine treats the family as an opaque key.

```csharp
public sealed record Signature(string Family, IReadOnlyDictionary<string, string> Facets);

public interface IClassifier
{
    Task<Signature> ClassifyAsync(ArtifactRef input, CancellationToken ct);
}
```

## 2. Executors

One interface, five tiers. The engine cannot distinguish them.

```csharp
public enum Tier
{
    Orchestrator = 0,   // high-thinking agent does the work and reviews it
    DelegateLarge = 1,  // large-model subagent, agent sees only the verifier summary
    DelegateMedium = 2, // mid-size model subagent
    DelegateSmall = 3,  // small-model subagent
    Codified = 4        // deterministic code, distilled from the tiers above
}

public sealed record StageRequest(
    string RunId,
    StageDefinition Stage,
    Signature Signature,
    IReadOnlyList<ArtifactRef> Inputs);

public sealed record StageResult(
    ArtifactRef Output,
    TraceRef? Trace,          // reasoning trace; the engine rejects an Orchestrator result without one
    Cost Cost,
    IReadOnlyList<Decision> Decisions);   // named choices the executor made, for distillation

public interface IExecutor
{
    Tier Tier { get; }
    Task<StageResult> ExecuteAsync(StageRequest request, CancellationToken ct);
}

public interface IExecutorResolver
{
    // Builds a runnable executor from its spec: model + playbook + tools for agent tiers,
    // a loaded assembly for Codified. Refs are immutable, so resolved executors are cached
    // for the process lifetime.
    Task<IExecutor> ResolveAsync(ExecutorRef reference, CancellationToken ct);
}

// An executor is data. The playbook lives on the spec, not on the request.
public sealed record ExecutorSpec(
    ExecutorRef Ref,
    Tier Tier,
    string? ModelClass,                  // large | medium | small; null for Codified
    PlaybookRef? Playbook,               // instructions, examples, rules; null for Codified
    IReadOnlyList<string> Tools,         // agent tools this executor may call; empty for Codified
    string? CodeAssembly,                // present for Codified
    ExecutorOrigin Origin,
    bool Promoted);                      // human gate; see section 7

public enum ExecutorOrigin { Seed, AgentDefined, Distilled }
```

`Decision` is the unit of learning. An executor names what it chose and why, in a form the
distiller can turn into a playbook rule.

```csharp
public sealed record Decision(string Key, string Choice, string Rationale, double Confidence);
```

## 3. Verifiers

Verifiers run on every stage result regardless of tier. Deterministic and model-backed verifiers
share the interface.

```csharp
public enum Verdict { Pass, Degraded, Fail }

public sealed record VerifierResult(
    string VerifierId,
    Verdict Verdict,
    string Reason,
    IReadOnlyList<Finding> Findings);

public interface IVerifier
{
    string Id { get; }
    bool IsDeterministic { get; }
    Task<VerifierResult> VerifyAsync(StageRequest request, StageResult result, CancellationToken ct);
}
```

The verifier runner orders deterministic verifiers first and stops at the first `Fail`, so a
schema violation never pays for a model-backed judgement. `Degraded` passes control flow but
publishes at half weight (section 8).

Verifier sets are the ground truth that moves policy. A stage with no verifiers can never be
promoted past `Orchestrator`.

## 4. Policy

One row per (document family, stage). A row is a ladder: the executor at each populated tier, which tier
is primary, and which tier is under shadow evaluation. `Orchestrator` is always populated; its
seed executor is the stage-scoped agent loop (section 6).

```csharp
public sealed record PolicyRow(
    string Family,
    string StageId,
    IReadOnlyDictionary<Tier, ExecutorRef> Ladder,
    Tier Primary,
    Confidence Confidence,            // primary pass rate
    Tier? Shadow,                     // next tier under evaluation; always a populated ladder slot
    Confidence ShadowConfidence,      // shadow pass rate
    DateTimeOffset UpdatedAt);

// Beta posterior on pass rate, time-decayed towards the prior.
public readonly record struct Confidence(double Alpha, double Beta)
{
    public static readonly Confidence Prior = new(1, 1);
    public double Mean         => Alpha / (Alpha + Beta);
    public double Observations => Alpha + Beta - 2;                 // effective count after decay
    public Confidence Decay(double factor) => new(1 + (Alpha - 1) * factor, 1 + (Beta - 1) * factor);
    public Confidence Observe(bool pass, double weight) =>
        pass ? this with { Alpha = Alpha + weight } : this with { Beta = Beta + weight };
}

public interface IPolicyStore
{
    // One read per run. A stage with no row gets the default: Ladder = { Orchestrator }, Primary = Orchestrator.
    Task<IReadOnlyDictionary<string, PolicyRow>> SnapshotAsync(
        string family, IEnumerable<string> stageIds, CancellationToken ct);
    Task<PolicyRow> GetAsync(string family, string stageId, CancellationToken ct);
    Task PutAsync(PolicyRow row, CancellationToken ct);
}
```

Routing is the snapshot. There is no separate router: the scheduler reads every row for the
run in one call and never touches the store again. `Ladder.Below(tier)` is the nearest populated
tier to the left; escalation and demotion both use it.

## 5. Scheduler

Pure control flow. No reasoning, no artifact inspection.

```csharp
public interface IScheduler
{
    Task<RunSummary> RunAsync(WorkflowDefinition workflow, ArtifactRef input, CancellationToken ct);
}

// Reference loop. A stage starts as soon as every dependency has an output; independent
// stages run concurrently. A stage failure cancels the rest of the run.
async Task<RunSummary> RunAsync(WorkflowDefinition wf, ArtifactRef input, CancellationToken ct)
{
    var runId   = Ids.New();
    var sig     = await classifier.ClassifyAsync(input, ct);
    var policy  = await store.SnapshotAsync(sig.Family, wf.Stages.Select(s => s.Id), ct);   // frozen for this run
    var outputs = new ConcurrentDictionary<string, ArtifactRef>();
    var shadows = new ConcurrentBag<Task>();
    using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);

    await wf.Stages.RunDagAsync(onError: run.Cancel, body: async stage =>
    {
        var row     = policy[stage.Id];
        var inputs  = stage.DependsOn.Count == 0 ? [input] : stage.DependsOn.Select(id => outputs[id]).ToList();
        var request = new StageRequest(runId, stage, sig, inputs);

        var record  = await ExecuteWithEscalationAsync(request, row, run.Token);
        outputs[stage.Id] = record.Output;

        if (row.Shadow is { } shadow && sampler.Take(t.ShadowSampleRate))
            shadows.Add(Quiet(AttemptAsync(request, row.Ladder[shadow], isShadow: true, run.Token)));
    });

    await Task.WhenAll(shadows);                    // bounded by stage budgets; never touches outputs
    return await ledger.SummariseAsync(runId, ct);
}

async Task<StageRecord> ExecuteWithEscalationAsync(StageRequest req, PolicyRow row, CancellationToken ct)
{
    var tier = row.Primary;
    while (true)
    {
        var record = await AttemptAsync(req, row.Ladder[tier], isShadow: false, ct);
        if (record.Passed) return record;
        if (tier == Tier.Orchestrator) throw new StageFailedException(req, record.Verdicts);
        tier = row.Ladder.Below(tier);              // one step left, this run only
    }
}

// Shared by primary, escalation and shadow. Every attempt is recorded and published, including
// failed ones: failed traces are what the distiller learns from.
async Task<StageRecord> AttemptAsync(StageRequest req, ExecutorRef executorRef, bool isShadow, CancellationToken ct)
{
    var executor = await resolver.ResolveAsync(executorRef, ct);
    var result   = await executor.ExecuteAsync(req, ct);
    var verdicts = await verifiers.RunAsync(req, result, ct);

    var record = new StageRecord(req.RunId, req.Stage.Id, executorRef, executor.Tier, result, verdicts, isShadow);
    await ledger.RecordAsync(record, ct);
    await outcomes.PublishAsync(Outcome.From(record, req.Signature.Family), ct);   // durable, non-blocking
    return record;
}
```

Escalation moves one populated tier left for this run only. Policy changes happen off the hot
path. Shadows are sampled, not universal: at a 20% sample a row with a shadow costs 1.2x, not 2x.

## 6. Discovery mode

Section 5 is the engine driving the agent. For a new document family the opposite is true: the agent
drives, and the engine records. Every document family has a run mode.

```csharp
public enum RunMode
{
    Discovery,   // agent loop owns control flow; engine provides tools and records
    Scheduled    // engine walks a mined WorkflowDefinition; agent is one executor among tiers
}

public sealed record DocumentFamilyPolicy(
    string Family,
    RunMode Mode,
    WorkflowRef? Workflow,        // present once a mined definition has been promoted
    Confidence Confidence);       // pass rate of the scheduled run's terminal output
```

### The agent loop

In `Discovery` mode the orchestrator is an agent loop with tools. The tools *are* the engine's
primitives, so nothing the agent does is invisible.

```csharp
public interface IAgentTools
{
    Task<DocumentFamilyContext> Context();                   // stage ids, executors, verifiers earlier runs used here
    Task<Stream>        ReadArtifact(ArtifactRef reference);
    Task<WriteResult>   WriteArtifact(string stageId, Stream content, ContractRef contract);
    Task<PlaybookRef>   DefinePlaybook(Playbook playbook);   // examples must be existing artifacts
    Task<string>        UploadCode(Stream code);             // returns the SHA-256 a Codified spec names
    Task<ExecutorRef>   DefineExecutor(ExecutorSpec spec);   // validated (tier, tools, assembly builds); usable now
    Task<string>        DefineVerifier(VerifierSpec spec);   // rubric-judged until a deterministic one is distilled
    Task<StageRecord>   Delegate(string stageId, ExecutorRef executor, IReadOnlyList<ArtifactRef> inputs);
    Task                Record(Decision decision);
}

public sealed record DocumentFamilyContext(
    IReadOnlyList<string> StageIds,
    IReadOnlyList<ExecutorSpec> Executors,
    IReadOnlyList<VerifierSpec> Verifiers);

public sealed record VerifierSpec(string Id, string StageId, string Rubric);
public sealed record WriteResult(ArtifactRef Ref, IReadOnlyList<VerifierResult> Verdicts);
```

Rules the engine enforces:

- The agent names a `stageId` on every write and every delegation. Ids are the agent's, but
  `Context()` offers the ids, executors and verifiers this document family has used before, so the agent
  reuses before it redefines.
- A stage's verifiers are the `VerifierSpec`s registered for it in this document family. They run on every
  `WriteArtifact` and every `Delegate`. Failures are returned to the agent, not escalated. In
  discovery mode the agent *is* the escalation.
- A `WriteArtifact` is a `StageRecord` at `Orchestrator` with the agent loop as executor. A
  `Delegate` is a `StageRecord` at the delegate's tier. Both carry verdicts.
- Every delegation goes through `Delegate`. A subagent the agent spawns any other way does not
  exist to the engine and earns no policy credit.
- The executor the agent delegates to is recorded as a `Decision` keyed `executor:{stageId}`, so
  policy learns which executor the agent trusted for which stage.
- An agent-defined executor is scoped to its document family until the miner carries it into a workflow.
- A Codified spec the agent writes runs in a sandbox with the stage's contract enforced at both
  ends. It is not `Promoted` until a human promotes it, so the scheduler will never route to it.

The ledger for a discovery run looks exactly like the ledger for a scheduled run: an ordered set of
`StageRecord`s with inputs, outputs, tiers, verdicts and traces. The DAG is not declared. It is
recorded. The agent loop's own transcript is the run-level trace on `RunSummary`.

### Mining a workflow

After every `MineAfterRuns` discovery runs the miner reads the document family's ledgers and proposes a
workflow plus the policy rows that seed it.

```csharp
public sealed record MinedWorkflow(WorkflowDefinition Workflow, IReadOnlyList<PolicyRow> Seeds);

public interface IWorkflowMiner
{
    // Stage ids, contracts and dependency edges present in at least MinSupport of the runs form
    // the DAG. Per stage, the executor the agent delegated to most seeds the ladder and becomes
    // the primary: its discovery StageRecords are its track record. Stages the agent always did
    // itself stay at Orchestrator.
    Task<MinedWorkflow?> MineAsync(string family, IReadOnlyList<RunSummary> runs, CancellationToken ct);
}
```

A mined workflow is a draft until a human promotes it. Once promoted, the document family runs in shadow:
discovery mode is still primary, and on a `ShadowSampleRate` sample of inputs the scheduler runs
the mined definition too. The scheduled run's terminal output is judged by the terminal stage's
verifiers, which publishes `WorkflowShadowPass` or `WorkflowShadowFail` against
`DocumentFamilyPolicy.Confidence`. Reaching `PromoteAt` over `MinObservations` flips the mode to
`Scheduled`.

### After the flip

Scheduled mode is section 5 unchanged. Stages at `Orchestrator` run a stage-scoped agent loop:
same tools, same rules, but it can only write that stage's output. Per-stage tier promotion then
proceeds as in section 8. The document family demotes the same way a stage does: if the terminal output's
pass rate falls below `DemoteAt`, the mode flips back to `Discovery` and mining starts over on the
new ledgers.

## 7. Data plane

```csharp
public interface IArtifactStore
{
    Task<ArtifactRef> PutAsync(string runId, string stageId, Stream content, ContractRef contract, CancellationToken ct);
    Task<Stream> GetAsync(ArtifactRef reference, CancellationToken ct);
}
// Content-addressed and immutable. Primary and shadow attempts share inputs by reference, and
// replaying a run is reading the ledger, never re-executing. Only Codified executors are
// deterministic; for agent tiers the hash identifies content, it does not predict it.

public sealed record StageRecord(
    string RunId, string StageId, ExecutorRef Executor, Tier Tier,
    StageResult Result, IReadOnlyList<VerifierResult> Verdicts, bool IsShadow)
{
    public ArtifactRef Output => Result.Output;
    public bool Passed => Verdicts.All(v => v.Verdict != Verdict.Fail);
}

public sealed record RunSummary(
    string RunId, Signature Signature, RunMode Mode,
    IReadOnlyList<StageRecord> Stages, TraceRef? Trace);

public interface IRunLedger
{
    Task RecordAsync(StageRecord record, CancellationToken ct);                       // append-only
    Task<RunSummary> SummariseAsync(string runId, CancellationToken ct);
    Task<IReadOnlyList<RunSummary>> RecentAsync(string family, RunMode mode, int take, CancellationToken ct);
}

public sealed record Playbook(
    PlaybookRef Ref,
    string Instructions,                       // prompt text
    IReadOnlyList<Example> Examples,
    IReadOnlyDictionary<string, string> Rules); // discovered rules, keyed by Decision.Key

public interface IRegistry
{
    Task<Playbook>           ResolveAsync(PlaybookRef reference, CancellationToken ct);
    Task<ExecutorSpec>       ResolveAsync(ExecutorRef reference, CancellationToken ct);
    Task<WorkflowDefinition> ResolveAsync(WorkflowRef reference, CancellationToken ct);

    Task<PlaybookRef> PublishAsync(Playbook playbook, CancellationToken ct);
    Task<ExecutorRef> PublishAsync(ExecutorSpec spec, CancellationToken ct);
    Task<WorkflowRef> PublishAsync(WorkflowDefinition workflow, CancellationToken ct);

    Task<string> PublishCodeAsync(Stream code, CancellationToken ct);   // returns the code's SHA-256
    Task<Stream> OpenCodeAsync(string hash, CancellationToken ct);
    Task<bool>   CodeExistsAsync(string hash, CancellationToken ct);

    Task PromoteAsync(ExecutorRef reference, CancellationToken ct);   // human gate
    Task PromoteAsync(WorkflowRef reference, CancellationToken ct);   // human gate
}
```

The human gate is precise. An `AgentDefined` executor at an agent tier is `Promoted` on publish:
the agent already ran it under verifiers. A `Distilled` executor, any executor with a
`CodeAssembly`, and any mined workflow need `PromoteAsync` before the scheduler will route to them.

### Where the data lives

| Data | Store |
|---|---|
| Artifacts, and the contract each was written against | S3, frozen, keyed by run, stage and SHA-256 |
| Registry content: playbooks, executor specs, workflows, code | S3, frozen, keyed by SHA-256 |
| Registry versions and promotion flags | Postgres `engine` schema |
| Run ledger: runs, stage records, decisions | Postgres |
| Policy rows and document family policies | Postgres |
| Document family executors and verifiers, mined workflow drafts | Postgres |
| Outcomes | SQS FIFO queue, grouped by document family |

Frozen content means a ref can never change meaning; promoting flips a row and never rewrites
content. The FIFO group keeps the updater the single writer for a family across every worker.
In-memory versions of every store exist for tests and are registered only on request.

## 8. Learning plane

Every signal becomes one event type. Outcomes are attributed to an executor, not a tier, so a row
can tell its primary, its shadow and an escalation fallback apart.

```csharp
public enum OutcomeKind
{
    VerifierPass, VerifierFail,                 // primary or escalation attempt
    ShadowPass, ShadowFail,                     // shadow attempt
    ExternalCorrection,                         // a human fixed the output; a fail at triple weight
    WorkflowShadowPass, WorkflowShadowFail      // family level, StageId is null
}

public sealed record Outcome(
    string Family, string? StageId, ExecutorRef Executor,
    OutcomeKind Kind, double Weight, DateTimeOffset At);      // Weight: 1, or 0.5 for a Degraded pass

public interface IOutcomeQueue
{
    Task PublishAsync(Outcome outcome, CancellationToken ct);
}

public sealed record Thresholds(
    double PromoteAt = 0.95,          // confidence mean to move one tier right
    double DemoteAt = 0.80,           // confidence mean to move one tier left
    int MinObservations = 20,         // effective observations before promotion
    double HalfLifeDays = 30,         // confidence decay
    double ShadowSampleRate = 0.2,    // fraction of runs that also execute the shadow
    int MineAfterRuns = 10,           // discovery runs between mining passes
    double MinSupport = 0.8);         // fraction of runs a stage or edge must appear in
```

The updater moves confidence and tiers. The queue groups outcomes by document family and the updater is a single
writer per partition, so no row is ever read-modify-written concurrently and no CAS is needed.

```csharp
public interface IPolicyUpdater
{
    Task ApplyAsync(Outcome outcome, CancellationToken ct);
}

// Reference update, stage level. Family-level kinds update DocumentFamilyPolicy.Confidence the same way.
async Task ApplyAsync(Outcome o, CancellationToken ct)
{
    var row   = await store.GetAsync(o.Family, o.StageId!, ct);
    var decay = Math.Pow(0.5, (o.At - row.UpdatedAt).TotalDays / t.HalfLifeDays);
    var pass  = o.Kind is OutcomeKind.VerifierPass or OutcomeKind.ShadowPass;

    if (o.Executor == row.Ladder[row.Primary])
        row = row with { Confidence = row.Confidence.Decay(decay).Observe(pass, o.Weight) };
    else if (row.Shadow is { } s && o.Executor == row.Ladder[s])
        row = row with { ShadowConfidence = row.ShadowConfidence.Decay(decay).Observe(pass, o.Weight) };
    else
        return;                                   // escalation fallbacks and retired executors do not move policy

    row = row switch
    {
        // Promote: the shadow becomes primary and keeps its track record as the new confidence.
        { Shadow: { } s } when Ready(row.ShadowConfidence)
            => row with { Primary = s, Confidence = row.ShadowConfidence, Shadow = null, ShadowConfidence = Confidence.Prior },

        // Demote: step to the nearest populated tier left; the old primary goes back to shadow.
        { Primary: > Tier.Orchestrator } when row.Confidence.Mean < t.DemoteAt
            => row with { Primary = row.Ladder.Below(row.Primary), Confidence = Confidence.Prior,
                          Shadow = row.Primary, ShadowConfidence = Confidence.Prior },

        _ => row
    };

    await store.PutAsync(row with { UpdatedAt = o.At }, ct);

    // Earned a shadow but has none: ask the distiller for a next-tier candidate. Off the row's
    // write path; the candidate arrives later as its own PutAsync when it is Promoted.
    if (row is { Shadow: null, Primary: < Tier.Codified } && Ready(row.Confidence))
        await distiller.RequestCandidateAsync(row, ct);
}

bool Ready(Confidence c) => c.Mean >= t.PromoteAt && c.Observations >= t.MinObservations;
```

Carrying the shadow's track record into promotion is what makes demotion safe without a second
threshold: a freshly promoted tier holds twenty or more passes, so one bad run cannot drop it
below `DemoteAt`, but a real regression will within a handful.

The distiller produces the next rung of a ladder. It writes drafts; a human promotes what needs
promoting.

```csharp
public interface IDistiller
{
    // Publishes the next-tier candidate for a row. Idempotent per (row, tier): a pending
    // candidate is not requested twice. When the candidate is Promoted it lands in
    // row.Ladder at its tier and becomes row.Shadow.
    //
    //   Orchestrator  -> DelegateLarge   traces + Decisions -> new Playbook + Distilled spec; human gate
    //   DelegateLarge -> Medium -> Small same playbook on the next model class; Promoted on publish
    //   DelegateSmall -> Codified        generated code + tests from the stable playbook; human gate;
    //                                    refused when the stage has no deterministic verifier
    Task RequestCandidateAsync(PolicyRow row, CancellationToken ct);
}
```

## 9. Invariants

- Policy is read once per run and frozen. Updates apply to the next run.
- Escalation moves to the nearest populated tier to the left, one step per attempt, and never persists.
- Every attempt is a `StageRecord`, including failed and shadow attempts. Only the primary's output leaves the stage.
- A shadow result is never used as an output. It only produces `ShadowPass` / `ShadowFail`.
- Only the primary and the shadow executor move a row. Escalation fallbacks earn nothing.
- Promotion requires a shadow track record and carries it. Demotion does not.
- A stage with no verifiers cannot leave `Orchestrator`. A stage with no deterministic verifier cannot reach `Codified`.
- Every artifact is immutable and content-addressed. Replaying a run is re-reading the ledger.
- Executors are opaque to the engine. Adding a tier is adding an `IExecutor`, not touching the scheduler.
- Humans promote mined workflows, distilled playbooks and all code. Nothing else waits on a human.
- In discovery mode the agent owns control flow and escalation. In scheduled mode the engine does. Never both in one run.
- A discovery run and a scheduled run produce the same ledger shape. The miner and the updater never know which produced a record.
- Delegation only exists through `Delegate`. Unobserved subagents earn no credit.
- The learning plane is never on the hot path. Publishing an outcome is a durable append; nothing in a run waits on the updater.
