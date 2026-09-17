# Does the worker fleet support segmentation?

*A review of [worker-fleet-plan.md](worker-fleet-plan.md) against
[theplot-segmentation-plan.md](theplot-segmentation-plan.md). The fleet names
segmentation as its first consumer (fleet §5, §8) but was written from a generic
worker model. Segmentation violates four of the fleet's stated invariants outright,
and breaks the assumption underneath its scaling maths. None of it is fatal; all of
it has to be decided before the module is written, because three of the fixes change
the module's interface.*

**Verdict: not as written.** Four blocking gaps, six design mismatches. The fleet's
*mechanism* (ASG, boot path, deploy anchor, IAM) fits segmentation well. Its
*scaling model* does not, because segmentation's throughput ceiling is provider
quota, not machines.

---

## 0. What already works — do not re-litigate these

Worth stating first, because the list below is long and the foundation is sound.

| | Why it fits |
|---|---|
| **Instance profile reuse** | Fleet §5 passes `aws_iam_instance_profile.instance`. Segmentation §19.4 attaches its S3 + SQS policy to `aws_iam_role.instance`. Burst nodes therefore already hold `s3:PutObjectRetention`, `sqs:ChangeMessageVisibility` and the rest — **no IAM work at all**, which fleet §7 did not realise (it lists `sqs:*` as a change). |
| **OTLP path** | SG already admits 4317–4318 `self = true`. A burst node reaches Host A's collector with no change. |
| **Deploy anchor** | Fleet §6 applies on `Role=services` first, then refreshes. Segmentation's `segmentation-migrations` pre-step (§23.3) runs there, before `push_manifest` publishes the tag — so a node booting afterwards never runs ahead of its schema. The ordering is already correct. |
| **Scale-in is cheap here** | Fleet §4 frets that killing a busy node returns 20 minutes of work to the queue. Segmentation checkpoints every superstep (N9, §13.2), so a killed node loses *one superstep*, not a run. The feedback loop fleet §4 describes is real but an order of magnitude weaker for this class. |
| **Secrets** | Fleet §2.9 requires in-process Secrets Manager reads over IMDS. Segmentation §21 already does exactly that with the `Seg_` prefix. |

---

## 1. Blocking — the fleet cannot start this worker

### B1. Segmentation is three queues and two consumers; the contract allows one

Fleet §1: *"one worker class = one image = one queue = one ASG"*. Fleet §2.1:
*"Long-poll exactly one queue... No queue discovery, no fan-out."*

Segmentation §19.3 creates `runs`, `runs-bulk` and `batch-poll`, and §20.2 registers
**two** hosted services on one process:

```csharp
builder.Services.AddHostedService<RunConsumer>();
builder.Services.AddHostedService<BatchPollConsumer>();
```

That is a direct contract violation, and it is not cosmetic — the whole §1 argument
(one backlog per ASG, honest scaling signal) collapses if one process drains three
incomparable queues.

**Fix — three registrations, one image, only one of which is a fleet:**

| Class | Queue | Tier | Why |
|---|---|---|---|
| `segmentation-realtime` | `runs` | Host B baseline **+ ASG 0–N** | The only lane where a machine converts to throughput. |
| `segmentation-bulk` | `runs-bulk` | Host B baseline **only, no ASG** | See D4 — the work is submitting and waiting, not computing. |
| `segmentation-batch-poll` | `batch-poll` | Host B baseline **only, no ASG** | A delayed message that wakes up to make one API call. Scaling it is absurd. |

The image stays one image; `image` is a per-class field in fleet §5 and nothing stops
three entries naming the same repo. What must change in the image: **which consumer
starts is decided by config, not by `Program.cs` registering both.** Add
`Worker__Role` ∈ `{runs, bulk, batch-poll}` and register exactly one
`BackgroundService` from it.

**What must change in the fleet plan:** §1 currently assumes every class has an ASG.
It needs the concept of a **baseline-only class** — a Host B footprint with no
launch template and no ASG. Fleet §4 already half-arrives here ("a Host B footprint
is not optional, only its size is"); this makes it a first-class registration, and it
means `worker_classes` needs an `asg = true|false` (or a separate
`baseline_only_classes` map) so the module can render nothing but a compose block.

### B2. Redis is unreachable from a burst node — and this is a correctness gap, not a convenience one

Segmentation §14.4 puts **every provider rate-limit reservation** in Redis under
`seg:budget:*`, as an atomic Lua sliding-window bucket shared across all workers. It
is the sole mechanism enforcing N2 (*429 rate < 1%*). A burst node that cannot reach
it either cannot make a single model call, or — worse, if someone "fixes" it with a
local fallback — every node reserves against its own private budget and five nodes
blow every provider limit simultaneously.

Three concrete problems, all verified in the repo:

1. **No published port.** `redis` in `deploy/docker-compose.host-services.yml:290`
   declares no `ports:`. It is reachable only on the compose network. The connection
   string `ConnectionStrings__redis: "redis:6379"` (§23.2) resolves to nothing on a
   burst node.
2. **No SG ingress.** `infra/network.tf:56` admits 8080–8083 and 4317–4318 `self = true`
   and nothing else. Fleet §7 proposes adding 5432 and **never mentions 6379**.
3. **No authentication.** The command line is
   `redis-server --maxmemory 256mb --maxmemory-policy volatile-ttl` — no
   `requirepass`, by design, because today nothing outside the compose network can
   reach it. Publishing it to the SG without a password puts an unauthenticated
   keyspace holding auth's cache in front of every instance in the group.

**Fix, in order of preference:**

- **(a) Put budget reservation behind `api`.** A `ReserveBudget` gRPC method on the
  existing 8080–8083 surface the SG already admits. No new port, no new SG rule, no
  Redis password, and it collapses D3's "do workers need Postgres directly" open
  question at the same time. Cost: one RPC (~1ms on a private IP) on the hot path of
  every model call, and `api` becomes load-bearing for the worker tier.
- **(b) Publish 6379, add `self = true` ingress, and set `requirepass` from
  Secrets Manager.** Lower latency, but it means a new secret, a compose change on a
  live box, and an auth-cache keyspace exposed group-wide.

Pick (a) unless the reservation round-trip measures badly. Either way this is a
**segmentation-plan change**, not just a fleet one: §14.4's "Redis on Host B is
already a shared, in-memory cache" reasoning is only valid while every worker is on
Host B.

### B3. `HOST_B_IP` is never seeded on a burst node

Fleet §3 seeds `HOST_A_IP` into `.env` and nothing else. Segmentation's worker needs
Postgres (`runs`, `run_phases`, `model_calls`, checkpointed loop counters) and,
per B2(a), `api`. Both are on Host B.

The mechanism exists — `deploy/deploy.sh:1269-1270` already handles both peer vars,
and Host A receives `HOST_B_IP` from cloud-init today — so this is a one-line
addition to the launch template plus a `host_b_ip` module input. But note the
second half: `deploy.sh:1294-1306` gates an apply on the peer IP being present,
keyed on `HOST_ROLE`. Fleet §7 already flags that `worker)` cases are needed in
*both* `HOST_ROLE` switches; make sure the peer-IP gate for `worker` demands
`HOST_B_IP` (not `HOST_A_IP`, which is what the `services` role checks).

If Postgres access stays direct, the 5432 `self = true` ingress fleet §7 lists is
required, plus a published port on the `postgres` service — which
`docker-compose.host-services.yml:154` does not have either. Routing through `api`
avoids both.

### B4. The env-var contract does not match, so "one compose file" does not hold

Fleet §3 asserts *"Adding a worker class adds no new compose file"* because the
generic service reads `Worker__QueueUrl` and `Worker__Concurrency`. Segmentation
§20.2 reads `Seg_Queues__Runs`, `Seg_Queues__Bulk`, `Seg_Queues__BatchPoll` and
`Seg_Queues__MaxConcurrentRuns`.

The claim is only true if every worker image honours the generic names. **Make the
fleet's two variables part of the §2 contract and have segmentation bind them** —
`Worker__QueueUrl` → the one queue this process drains, `Worker__Concurrency` →
`MaxConcurrentRuns`. Keep `Seg_*` for everything the fleet does not know about
(storage, routing, pipeline, providers), which is most of it.

Related, and a bug in the segmentation plan independent of the fleet: the
`RunConsumer` sketch (§13.4) receives with `MaxNumberOfMessages = 1` and then
`await`s `RunOrResumeAsync` inside a sequential `foreach`. **As written its
concurrency is 1 regardless of `MaxConcurrentRuns = 8`.** `Worker__Concurrency`
needs something real to bind to — a semaphore-bounded dispatch loop.

---

## 2. The scaling model is wrong for this workload

### D1. Machines are not the constraint — provider quota is. The fleet has no guard for it

This is the substantive disagreement between the two documents.

Fleet §4 computes `wanted = ceil(backlog_seconds / (target_drain_secs × concurrency_per_node))`
and clamps it with three guards. Every one of them assumes **a machine converts to
throughput**.

Segmentation §29.3 says the opposite, explicitly: *"The worker is I/O-bound — it
waits on providers, not on CPU... provider quota binds long before Host B does."*
And §29.2 makes it arithmetic: `docsPerMinute ≈ min over pools of (poolTPM × share) / tokensPerDoc`.

So when the pools are saturated, a fifth burst node adds **zero** throughput. It
boots, long-polls, receives a run, and parks in `MaxRoutingWait` (default **10
minutes**, §20.3) waiting on a Redis reservation that the other four nodes are
consuming. Then the backlog does not fall, so the fleet asks for more. You pay
`~3 min boot + 10 min idle + 15 min before scale-in` per machine to do nothing —
and the run is *slower*, because `MaxRoutingWait` expiry returns it to the queue
(§28, "All providers rate-limited").

**Guard 4, and it belongs in fleet §4 next to the other three:**

```
wanted ≤ ceil(available_provider_slots / concurrency_per_node)
```

where `available_provider_slots = Σ over eligible pools of (MaxConcurrency − in_use)`,
read straight out of the same Redis buckets §14.4 already maintains. The scaler takes
`min(work_based_wanted, quota_based_wanted)`.

The publisher is the natural home for it: it lives on Host B (fleet §4 requires
that), and Redis and Postgres are both local to it there. So it publishes **two**
metrics, not one — `backlog_seconds` and `quota_headroom_slots` — and the second is
nearly free to compute.

This also answers fleet §9's *"where does the publisher run"*: for segmentation it is
`api`, which already reads run state from Postgres (§7.3) and would already hold the
budget surface under B2(a). One `PutMetricData` timer in a service that is always on.

### D2. A rolling average per job type is meaningless for this class

Fleet §2.6 makes per-type elapsed-time recording a *hard* requirement, and fleet §9
already worries the right worry: *"if a type turns out to be two populations wearing
one name, the average describes no real job."*

Segmentation is not two populations — it is a continuum spanning three orders of
magnitude, and the plan says so in its own exit criteria. M2's target is *"clean
Markdown completes end-to-end with **zero model calls**"* — seconds. A degraded
500-page scan runs thousands of label windows plus a repair loop plus augmentation
over every paragraph — tens of minutes to hours. Both are `job_type =
segmentation-run`. Their mean describes nothing.

**Fix: estimate from size at enqueue, not from a per-type mean.**

```
estimated_seconds = a[condition] + b[condition] × input_bytes
```

`Condition` (Clean / Partial / Degraded / Conversational) is on the manifest at
submit time (§8.4) and `input_bytes` is known from the presigned upload before the
message is sent — so both inputs exist at enqueue, which is exactly when the fleet
needs them. Fit `a` and `b` per bucket by regression over completed `run_phases`
rows; the same table already carries started/finished per phase.

Consequence for the fleet plan: §2.6 should require *"record elapsed time against a
recorded size measure"*, and §4 should describe the signal as a per-class estimator
function rather than a per-type average. Fleet §9's first open question is then
answered for this class rather than left open.

### D3. A single document cannot be spread across machines at all

Segmentation §7.4: *"One MAF workflow instance per run; one SQS message per run."*
§13.4: `MaxNumberOfMessages = 1`, *"fan-out is inside the workflow"*.

Fleet §4's "You cannot beat the longest single job" therefore binds hard: **per-run
latency is completely unaffected by the fleet.** Burst nodes help only when many runs
are queued concurrently. If ThePlot's on-screen promise (segmentation open question
5) is about one user watching one upload, the fleet contributes nothing to it.

Worse, guard 2 (`wanted ≤ total_work ÷ longest_job`) will frequently evaluate to 1 in
the realistic interactive case — a couple of users, one big document each. And
guard 3 kills it outright: a 10-minute run costs ~28 minutes billed.

**Two honest options, and the plans should choose explicitly:**

- **Accept it.** The fleet is for *bulk import* concurrency, not interactive latency.
  Then `segmentation-realtime`'s ASG is mostly dormant and the Host B baseline does
  the real interactive work — which argues for D5 below.
- **Make window-labeling its own class.** Phase 3 is already an embarrassingly
  parallel fan-out over windows (§13.1 `LabelWindowExecutor × N`), each window is
  independently idempotent and keyed (§13.3 checks `ExistsWithKeyAsync` before
  calling), and the artifacts already land as one S3 object per window. Pushing
  windows onto their own queue is the *only* design here that makes one document
  faster by adding machines. It is a real change to §13 (the planner enqueues instead
  of fanning out in-process; the merge waits on artifacts rather than on a fan-in),
  and it is also the thing fleet §4's "split long and short into separate classes"
  advice is pointing at.

I would not build the second in v1, but the plans should record that the first is a
deliberate limitation rather than an oversight.

### D4. The bulk and batch-poll lanes must never drive an ASG

Segmentation §14.8: for `Priority = Bulk`, the actual token work goes to **provider
batch APIs**. The worker submits, writes a `batch-poll` message with `DelaySeconds`,
and exits. Explicitly: *"a waiting batch costs no worker time at all."*

So the compute profile is: milliseconds of submit, hours of nothing, milliseconds of
poll. Under fleet §4 this is pathological in both directions —

- `backlog_seconds` is near zero while 10,000 documents are in flight, so the primary
  says "no capacity needed" (correct, but for a reason the model does not represent);
- the **backstop** (`depth > 200` or `age > 15 min`, fleet §4) fires immediately on a
  10k-document import and starts machines that have nothing to do. Fleet explicitly
  frames a backstop firing as *"a bug report"* — here it would fire every time.

And a delayed `batch-poll` message is invisible until its delay expires, so a
scale-to-zero fleet watching that queue sees nothing, scales to zero, and the message
becomes visible with no consumer. **Both lanes are baseline-only.** Per B1.

### D5. Concurrency 1 on the Host B baseline is the wrong cap for an I/O-bound worker

Fleet §1's table fixes Host B at `Worker__Concurrency = 1`, reasoning from a
CPU/memory-heavy worker on a shared 2 vCPU box. Segmentation §29.3 starts at **8** on
the same box, and is right to: the process is asleep on HTTP for almost all of its
wall-clock. Concurrency 1 would cut interactive throughput eightfold to protect a CPU
that is not the constraint.

The real cap is memory — §29.3: *"each run holds one document's lines in memory
during phases 0–4"* — which is a per-class number, not a constant.

**Fix:** make baseline concurrency a per-class field (`baseline_concurrency`)
defaulting to 1, with the fleet's three-baseline-worker budget expressed in **MB**
rather than in slots. Segmentation's entry sets 8 and a hard `mem_limit`; the
`mem_limit` is what protects Keycloak and Postgres, not the concurrency number.

### D6. `claim_timeout = 900` is below segmentation's p99, and the lifecycle drain is worse

Fleet §5's example registers `claim_timeout = 900  # > p99 job duration`. Segmentation
sets `VisibilityTimeout = 900` too (§13.4) — but renews it with a heartbeat precisely
*because* runs outlive it. A degraded 500-page document is not a 15-minute job.

Two knock-ons for the module:

- `claim_timeout` must be documented as *the heartbeat interval's backstop*, not as a
  bound on job duration — or segmentation's heartbeat and the fleet's claim column
  will be two mechanisms disagreeing (see C1).
- The **lifecycle hook** drain budget (fleet §4) has to cover a checkpoint, not a
  run. Here that is genuinely short — one superstep — so set the hook to *"finish the
  current superstep, checkpoint, complete the action"*, which is seconds to a couple
  of minutes, not the 2-hour ASG ceiling. Segmentation §23.4 already asks for
  `stop_grace_period: 60s` for the same reason; reuse that number.

---

## 3. Smaller inconsistencies, all cheap to fix

| # | Issue | Fix |
|---|---|---|
| **C1** | **Two redelivery mechanisms.** Fleet §4 ("Losing work") says once the pending list is in Postgres, a `claimed_by`/`claimed_until` lease plus a reaper replaces SQS visibility. Segmentation §13.4 relies on the visibility heartbeat and has no claim columns in `runs` (§8.4). | Pick one. For segmentation, **keep the SQS heartbeat** — it exists, it is tested by M6's kill-and-resume, and checkpoints make the redelivery window cheap. Then fleet §4's lease/reaper becomes optional per class rather than implied, and fleet §9's *"is SQS still earning its place"* resolves to **yes** for this class. |
| **C2** | Fleet §5's example registers `image = "protofast-segmentation-worker"` on a `c7g.xlarge`. Segmentation §23.1 builds `protofast-segmentation`, and the workload is I/O-bound with per-run memory. | `protofast-segmentation` on `m7g`/`r7g`. Compute-optimised is the wrong shape for a process that is asleep on HTTP. |
| **C3** | Segmentation §23.2 mounts `internal-jwt-pub` into the worker; fleet §3 promises worker user_data with *"no secret fan-out"*. | The worker publishes no port and is dialled by nothing (§7.1) — it does not verify inbound JWTs. Drop the secret from the worker block. If B2(a) makes the worker *call* `api`, it needs to **mint** a token, not verify one, which is a different key and a real decision. |
| **C4** | Burst nodes share `aws_iam_role.instance`, so they can read all of `protofast/app` — including `Auth_DbPassword` and Keycloak secrets. | Acceptable for now (same trust domain, same SG), but say so in fleet §2.9 rather than leaving it implied. A per-class role with a prefix-scoped secret read is the clean version if the blast radius matters later. |
| **C5** | The fleet's `versions.env`-from-S3 boot path (§3) assumes the tag is published before a node boots. Segmentation's apply runs migrations first (§23.3), then `push_manifest`. | Already correct. Worth an explicit note in fleet §6 that the ordering is load-bearing for classes with migrations. |
| **C6** | Fleet has no dev story; segmentation's `aspire run` starts the worker as a host process against LocalStack (§22.1). | Fine — the burst tier simply does not exist in dev. But B4's `Worker__*` variables must be set by the AppHost too, or dev and prod diverge on exactly the variables the fleet owns, which breaks the plan's own "same variable names" rule (§1). |

---

## 4. What to change, and where

**In `worker-fleet-plan.md`:**

1. §1 — add **baseline-only classes** (Host B footprint, no ASG). Needed by two of
   segmentation's three lanes.
2. §1 — `baseline_concurrency` per class instead of a fixed `1`; express the Host B
   budget in MB.
3. §2 — add `Worker__Role` to the contract (which consumer starts), and state that
   `Worker__QueueUrl` / `Worker__Concurrency` are binding names the image must honour.
4. §2.6 — require elapsed time **paired with a size measure**, not a bare per-type
   average.
5. §3 — seed `HOST_B_IP` in the worker `.env`; `worker)` peer-IP gate keys on it.
6. §4 — add **guard 4** (`wanted ≤ available_provider_slots / concurrency_per_node`)
   and make the publisher emit a second metric for it.
7. §4 — state that classes whose work is *waiting* (provider batch APIs) are
   baseline-only, and that the native backstops must be disabled for them.
8. §5 — fix the segmentation example: image name, instance family,
   `claim_timeout` semantics.
9. §7 — drop `sqs:*` and `s3` from the IAM row: segmentation §19.4 already attaches
   them to the shared instance role. Keep `SetInstanceHealth`, `PutMetricData`,
   `CompleteLifecycleAction`.
10. §9 — the first and fourth open questions are answered for this class (size-based
    estimator; access via `api`). The third is answered too: the publisher is `api`.

**In `theplot-segmentation-plan.md`:**

1. §14.4 — Redis budget reservation must survive workers that are not on Host B.
   Prefer a gRPC surface on `api`.
2. §20.2 / Program.cs — one consumer per process, selected by config.
3. §13.4 — the consumer loop is sequential; it needs bounded concurrent dispatch for
   `MaxConcurrentRuns` (and `Worker__Concurrency`) to mean anything.
4. §29.3 — *"the answer is a bigger Host B or a second worker box"* should now point
   at the fleet, with the quota ceiling (D1) stated as the reason the second box may
   not help.
5. §30 — the fleet is an M7 concern ("Routing & scale"), not M1. M1 should build the
   queues and the Host B baseline only.

**Decide before either module is written** (these change interfaces, not just prose):

- Does the worker reach Postgres and Redis **directly**, or through `api`? This one
  answer settles B2, B3, the 5432/6379 SG rules, and where the publisher lives.
- Is per-document latency in scope for the fleet? If yes, window-labeling becomes its
  own class (D3) and that reshapes §13.
