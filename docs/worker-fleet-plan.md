# Worker fleet — scale-to-zero burst compute

*A generic worker tier: an always-on baseline worker on Host B plus a 0–5 instance
ASG that wakes on backlog and returns to zero when idle. It scales on **estimated
seconds of work waiting**, not on message count, and is tuned to move smoothly
rather than in spikes (§4). This document specifies the **infrastructure**, not any
particular worker. Segmentation is the first consumer of it, not its definition.*

*Revised after checking the design against that first consumer — see
[worker-fleet-segmentation-fit.md](worker-fleet-segmentation-fit.md) for the gap
analysis this revision closes. The load-bearing changes: baseline-only classes (§1),
one image may back several classes (§1, §5), the scaling signal is a size-based
estimator rather than a per-type average (§4), and **guard 4** — a machine only
helps if the bottleneck is the machine (§4).*

---

## 1. The decision that shapes everything else

The open question was how workers are configured: one application polling many
queues, one application per queue with several competing on a box, or an
asymmetric split (multi-queue baseline, single-queue burst).

**Decision: one worker class = one image = one queue = one ASG. Symmetric across
both tiers.** The same image runs on Host B and on a burst node; only two env vars
differ (`Worker__Concurrency`, and whether scale-in protection applies).

Note "one image" is not "one image per class" — one *image* may back several
classes, each pinned to its own queue by `Worker__QueueUrl` and its own consumer by
`Worker__Role` (§2). Segmentation is exactly this: three lanes (`runs`,
`runs-bulk`, `batch-poll`), one binary, three registrations. What the decision
forbids is one *process* draining more than one queue.

Three reasons, in order of weight:

**An ASG scales on one backlog.** A fleet's desired capacity is a function of a
single set of CloudWatch alarms. A process polling several queues makes "how many
instances do I need" unanswerable — you would be summing incomparable backlogs
(400 quick thumbnail jobs and 3 twenty-minute segmentation jobs are not 403 of
anything). Single-queue keeps the scaling signal honest and lets each class pick
its own instance type: a memory-heavy class and a cheap burst class do not have to
share a shape.

**Asymmetry between tiers is a bug factory.** If the baseline worker is
multi-queue and burst workers are single-queue, the same job runs under two
different runtime configurations, and you inherit a class of defect where
behaviour depends on which tier happened to pick the message up. It also doubles
the code paths under test. The image must be byte-identical on both tiers.

**Blast radius.** One poison message that OOMs a multi-queue process takes down
every class's consumer on that box. Single-queue contains it to its own class.

### What that means concretely

| | Host B (`Role=services`) | Burst node (`Role=worker`) |
|---|---|---|
| Worker containers | **one per baseline-enabled class** | **exactly one** — the class its ASG belongs to |
| `Worker__Concurrency` | `baseline_concurrency`, default `1` | per-class, typically 4–8 |
| Resource caps | hard `cpus` / `mem_limit` | the whole box |
| Lifetime | always on | 0 → 5, back to 0 |
| Purpose | absorb the trickle, kill cold-start on the first job | drain a real backlog |

So a box *does* contain several competing workers — but only Host B, only for
classes that opt in, and only inside hard caps. A burst node never runs more than
one class.

**Baseline concurrency is per class, not a constant.** The default of `1` is right
for a CPU- or memory-bound worker sharing 2 vCPU with a Keycloak JVM. It is wrong
for an I/O-bound one: a worker that spends its wall-clock asleep on an HTTP call to
a model provider gets no protection from a concurrency of 1, it just runs eight
times slower than it could. Segmentation sets 8 for exactly this reason.

**What actually protects the box is `mem_limit`, not the concurrency number.** So
set concurrency from what a slot costs in memory and let the cap enforce it, rather
than pretending one slot is a universal unit of load.

### Baseline-only classes

Not every class wants an ASG. A class whose work is *waiting* rather than computing
— submitting to a provider's batch API and polling it hours later — converts a
burst machine into pure cost: the primary signal reads near zero while thousands of
items are in flight, and the native backstops (§4) fire on queue depth and start
machines with nothing to do. A delayed message is also invisible to a fleet watching
its queue, so a scale-to-zero ASG sees nothing, sits at zero, and the message
becomes visible with no consumer.

So a class registration carries `asg = true | false`. With `asg = false` the module
renders **no launch template, no ASG, no scaling policies and no alarms** — just the
Host B compose block, the queue and its DLQ. It is a first-class registration, not a
degenerate one: two of segmentation's three lanes are baseline-only.

### Baseline is opt-in, and it is a scarce slot

Host B is a `t4g.medium` (2 vCPU / 4 GB) already carrying a Keycloak JVM,
Postgres, Redis and three .NET services. Every baseline worker is ~150–500 MB that
box does not get back. So `baseline = true` is a per-class declaration, not a
default. A class with a steady trickle or a latency-sensitive first job takes the
slot; a rare batch class does not, and accepts a cold start.

Budget: **~1.5 GB of `mem_limit` across all baseline workers** on the current
instance size — roughly three at 500 MB, or two at 500 MB plus one at 250 MB.
Counting slots rather than megabytes is what lets a class with
`baseline_concurrency = 8` quietly eat the budget of three. Past the limit, resize
Host B or drop baselines — do not quietly overcommit the box that holds the auth
database.

One qualification from §4: a *worker* on Host B stays optional, but a **Host B
footprint** does not. The scaling signal has to be published by something that is
alive when the fleet is at zero, and that can only be Host B. A class without a
baseline worker still needs a small publisher there. Opt-in governs the size of
the footprint, not its existence.

---

## 2. The worker image contract

This is the whole API between the infra and a worker. Anything satisfying it can
be a worker class; nothing else is assumed.

A worker image **must**:

0. **Honour the fleet's variable names.** `Worker__QueueUrl`, `Worker__Concurrency`
   and `Worker__Role` are binding — the generic compose file in §3 sets exactly
   these, and the claim that adding a class adds no new compose file holds only
   while every image reads them. An image is free to keep its own prefix for
   everything the fleet does not know about (storage, routing, providers), which is
   most of its configuration; it must not rename these three. They must also be set
   by the AppHost in dev, or the fleet's own variables become the one place dev and
   prod diverge.
1. **Long-poll exactly one queue**, read from `Worker__QueueUrl`. No queue
   discovery, no fan-out. Where one image backs several classes, `Worker__Role`
   selects which consumer starts — **exactly one per process**. Registering every
   consumer and letting configuration decide which queue each reads is the same
   violation wearing a disguise.
2. **Respect `Worker__Concurrency`** — the maximum messages in flight in-process.
   This means bounded concurrent dispatch, not a sequential receive-and-await loop:
   a consumer that awaits each message before the next receive has a concurrency of
   1 whatever the variable says, and the fleet will size itself against a throughput
   number the worker cannot deliver.
3. **Hold its claim while working** (heartbeat), so a job longer than the claim
   timeout is not handed to a second worker underneath the first.
4. **Be idempotent.** This is the load-bearing requirement. On scale-in, a
   terminated node's claim expires and the job is retried elsewhere. Phase 1 does
   not prevent that — it relies on it.
5. **Mark the job done only after the work is durable**, never before.
6. **Record every job's elapsed time on completion, paired with a size measure**
   — the input bytes, line count, page count or whatever the class's cost actually
   scales with, recorded alongside the type. This is what feeds the estimator §4
   scales on. Without it the fleet has no primary signal and falls back to the
   backstop alarms — so this is a hard requirement, not telemetry.

   A bare per-type average is not enough for any class whose durations span orders
   of magnitude, and most interesting classes do. Segmentation's cheapest run
   finishes in seconds with zero model calls and its most expensive runs for hours;
   their mean describes no real job. The size measure is what turns that into a
   usable `estimated_seconds = f(size)` (§4).
7. **Report its own remaining-time estimate for running jobs**, so in-flight work
   counts toward the backlog (§4, "the feedback loop"). A crude estimate —
   `estimate_for_size − elapsed`, floored at zero — is sufficient and needs no
   per-worker code beyond emitting `started_at`.
8. **Exit non-zero on unrecoverable startup failure**, so the container's restart
   loop and the node's health reporting can see it.
9. **Read secrets in-process from Secrets Manager**, same as `auth` does — via the
   instance role over IMDS, with `AWS_REGION` supplied. No secret files.

   State the consequence rather than leaving it implied: burst nodes share
   `aws_iam_role.instance` with Host A and Host B, so a worker can read **all** of
   `protofast/app`, including the auth and Keycloak database passwords. That is
   accepted for now — same trust domain, same security group — but it is the reason
   a class must never mount a secret *file*: a file is a fan-out step in cloud-init
   that a scale-to-zero node has no operator to fix. If the blast radius ever
   matters, the clean version is a per-class role with a prefix-scoped read.

A worker image **must not**: know its own instance id, call the Auto Scaling API,
manage lifecycle hooks, or care which tier it is on. All of that is the host's job.
Keeping AWS lifecycle knowledge out of the image is what makes a new worker class
cheap to write.

Job *state* (progress, results, history, timings) belongs in Postgres, owned by
the class. The queue carries a job id and nothing else — and per §4 the pending
list in Postgres, not the queue, is what the scaling signal is computed from.

---

## 3. How a burst node actually starts a container

The ASG never starts a container. It boots an instance; cloud-init does the rest.
This is the existing Host A / Host B boot path with the stateful parts removed.

```
alarm → desired=1 → launch template → EC2 boots AL2023
  └─ cloud-init runs user_data (templates/user_data.host_workers.sh.tftpl):
       1. ${common_setup}   docker + amazon-ecr-credential-helper + compose plugin
       2. seed .env         ECR, AWS_REGION, HOST_ROLE=worker,
                            HOST_A_IP, HOST_B_IP,
                            WORKER_CLASS, WORKER_IMAGE, WORKER_QUEUE_URL,
                            WORKER_ROLE, WORKER_CONCURRENCY, ASSETS_BUCKET
       3. aws s3 cp         versions.env
                            docker-compose.host-workers.yml → docker-compose.yml
                            deploy.sh
       4. systemctl start protofast-worker.service
            └─ ExecStart=/opt/protofast/deploy.sh bootstrap → docker compose up -d
                 └─ pull: credHelpers → ecr-login → IMDSv2 → instance profile
                      └─ container long-polls its queue
```

Three things carry that chain, and each is already proven on the two existing
hosts:

- **`versions.env` from S3** is how a node with no operator attached learns its
  image tag. `deploy.sh` `push_manifest` publishes it after every apply
  (`deploy/deploy.sh`), and cloud-init pulls it. This mechanism already exists —
  it is why Host B self-heals on replacement. Workers need nothing new here, and
  in particular **do not need SSM Parameter Store**.
- **The instance profile on the launch template**, plus
  `http_put_response_hop_limit = 2`, is what lets the ECR pull authenticate with
  no credentials on disk.
- **The systemd unit** is what runs the container. `user_data` executes once per
  instance and never again, so a reboot would otherwise come back empty; and
  `ExecStop` is the only hook for draining on shutdown.

### Reaching Host B's state: through `api`, not through the database

A burst node is not on Host B's compose network, so `postgres` and `redis` do not
resolve for it, and neither service publishes a host port today. Two ways to close
that, and the choice is load-bearing enough to settle here rather than in §9.

**Decision: workers reach state through the service tier on 8080–8083**, which the
security group already admits `self = true`. No new ingress rule, no published
database port, no password on a Redis that today needs none because nothing outside
one compose network can reach it. The alternative — publishing 5432 and 6379 to the
group — also means putting `requirepass` on the cache that holds auth's sessions and
adding a secret to do it, which is a live change to the box holding the auth
database for the benefit of a tier that does not exist yet.

This matters beyond the DB boundary for any class whose *correctness* depends on
shared state. Segmentation's provider rate-limit reservations live in one Redis
keyspace precisely so that all workers draw down one budget; a burst node that
cannot reach it either makes no model calls at all or — if someone "fixes" it with a
local fallback — five nodes each reserve against a private budget and blow every
provider limit at once. Shared-state reachability is a day-one requirement for such
a class, not an optimisation.

`HOST_B_IP` is therefore seeded in the worker's `.env` above. The mechanism already
exists — `deploy.sh` handles both peer variables and Host A receives `HOST_B_IP`
from cloud-init today — but note that the peer-IP gate keys on `HOST_ROLE` and each
role checks only the variable *its* compose interpolates. The `worker)` case must
gate on `HOST_B_IP`, not on the `HOST_A_IP` that `services` checks. `HOST_A_IP` is
still seeded, for OTLP to the collector on 4317–4318 — also already admitted.

Revisit only if a reservation round-trip measures badly on the hot path.

### One compose file serves every class

A burst node runs exactly one worker, so the class can come from `.env` rather
than from the file. `deploy/docker-compose.host-workers.yml` holds a single
generic service:

```yaml
services:
  worker:
    image: ${ECR}/${WORKER_IMAGE}:${WORKER_TAG}
    restart: unless-stopped
    environment:
      <<: *dotnet-env
      Worker__QueueUrl: "${WORKER_QUEUE_URL}"
      Worker__Concurrency: "${WORKER_CONCURRENCY:-4}"
      Worker__Role: "${WORKER_ROLE}"
```

Adding a worker class adds **no new compose file** — but only because every image
honours those three names (§2.0). Host B's `docker-compose.host-services.yml` is the
exception: it is hand-maintained and gets one explicit service block per
baseline-enabled class, at `baseline_concurrency` with hard caps. Baseline-only
classes appear *only* there.

---

## 4. Scaling

### The signal: estimated seconds of work waiting

Each class keeps a rolling record of how long its work actually takes, and
publishes one number: **the total time the queued work is expected to need, plus
the time still remaining on jobs already running.** Capacity is work over time, so
this is the only signal here that measures the thing being scaled. Message count
does not, because messages are not the same size.

It also lets you compute capacity instead of tuning thresholds by feel:

```
wanted = ceil(backlog_seconds / (target_drain_secs × concurrency_per_node))
```

**The estimator is a function of size, not an average over a type name.** A rolling
mean per `job_type` only describes a real job when that type's durations cluster,
and for most classes worth building a fleet for they do not. Use the cheapest thing
that does cluster:

```
estimated_seconds = a[type] + b[type] × size
```

fitted by regression over completed rows. Two properties make this workable rather
than aspirational: both inputs are known **at enqueue**, which is exactly when the
signal needs them, and the fit is refreshed from the same completion records §2.6
already requires. For segmentation, `type` is the condition bucket the manifest
already carries at submit and `size` is the uploaded object's byte count, known from
the presigned upload before the message is sent.

Where a type genuinely does cluster, `b = 0` and this degrades to the flat average
with no extra work. Where it does not, the flat average was never going to scale
anything correctly.

Two signals we are *not* using as primary, and why:

- **Message count** is meaningless when jobs differ in cost. Four hundred quick
  jobs and three long ones are not 403 of anything.
- **Age of oldest message** measures latency, not volume: one long job sitting for
  three minutes looks identical to five hundred jobs sitting for three minutes.
  It stays as a backstop below, not as the main input.

Estimated-backlog also removes a problem the earlier draft had to work around: the
baseline worker on Host B holds the message count down, so a count-based alarm
never fires even while the fleet falls behind. A work-time number does not care
who is draining it.

### The cost: something has to publish it

AWS publishes queue depth and message age whether or not anything of ours is
alive. This number does not exist unless a process of ours computes and sends it.
That is the real price of the better signal, and it fails in the worst possible
way: if the publisher stops, the alarm sees **no data**, not "lots of work." The
fleet stays at zero, the queue grows, and nobody is paged.

There is no safe setting to paper over it. Treat missing data as breaching and a
wedged publisher pins the fleet at max forever; treat it as ignorable and the
fleet silently never wakes.

Two consequences to accept up front:

1. **The publisher runs on Host B.** It cannot run on a burst node — there are
   none at rest. So every class that wants this signal needs *something* of its
   own on Host B. For a class with a baseline worker that is free: the baseline
   worker publishes. For a class without one, it is a small always-on publisher,
   which weakens "baseline is opt-in" from §1 into "a Host B footprint is not
   optional, only its size is."

   Cheaper still, and the better answer where it applies: **a service that is
   already always on and already reads the class's job state can be the publisher**
   — one timer, no new container, no new memory against §1's budget. For
   segmentation that is `api`, which already reads run state from Postgres to serve
   status, and which under §3's decision also fronts the shared-budget keyspace the
   ceiling metric is computed from. Both numbers are local to it.
2. **The queue alone cannot answer the question.** Summing durations over waiting
   jobs requires knowing what is waiting, and SQS will not enumerate without
   consuming. So the pending-job list must live in Postgres, with SQS as the
   wake-up nudge. That matters more than it sounds — see "Losing work" below.

**Therefore the AWS-native metrics stay, as a backstop.** They cost nothing extra
and they cannot break when our publisher breaks:

| Role | Metric | Condition | Action |
|---|---|---|---|
| **Primary** | `backlog_seconds` (ours) | see ranges below | scale |
| **Ceiling** | `downstream_slots` (ours) | see guard 4 below | clamp `wanted` |
| **Backstop** | `ApproximateAgeOfOldestMessage` **or** `ApproximateNumberOfMessagesVisible` | generously set — `age > 15 min` or `depth > 200` | step +1 |
| **Watchdog** | `backlog_seconds` | no data for 10 min | **notify, do not scale** |

The backstop exists solely to catch a stale or wrong primary. Set it loose enough
that it never fires in normal operation — when it fires, that is a bug report. It
follows that **a class whose backstop would fire routinely must not have one**, and
usually must not have an ASG either: a lane that enqueues ten thousand items and
then waits on someone else's batch API breaches `depth > 200` on every import while
needing no capacity at all. That is a baseline-only class (§1), and the module
renders no alarms for it.

### You cannot beat the longest single job

A job does not split across machines, so the earliest anything can finish is the
duration of the longest queued job. Raw arithmetic ignores this: 40 minutes of
work with a 10-minute target asks for 4 machines, but if that work is one
30-minute job and one 10-minute job, two machines finish in 30 minutes and four
finish in 30 minutes too.

Four guards clamp `wanted`, applied in order:

| # | Guard | 30 + 10 example |
|---|---|---|
| 1 | never more machines than waiting jobs | → 2 |
| 2 | never more than `total_work ÷ longest_job` | `40 ÷ 30` → 2 |
| 3 | only add a machine if its share of work exceeds its overhead | → **0** |
| 4 | never more machines than the downstream bottleneck can feed | n/a here |

Guard 3 is the one usually skipped, and usually decisive. A burst machine costs
boot time plus the idle wait before shutdown before it does anything useful:

```
~3 min boot  +  10 min useful work  +  15 min idle before scale-in
= ~28 min billed for 10 min of work
```

So for the 30 + 10 case the answer is **do not scale at all** — let the baseline
worker take both, finishing in 40 minutes. Shortening the idle wait from 15 to 5
minutes changes that verdict (18 min billed instead of 28); the trade is more
churn on a bumpy queue. Set it per class from measured job times, not from the
placeholder here.

A corollary worth stating plainly: **if one job runs longer than the target drain
time, the target is unreachable and no amount of scaling will reach it.** Guard 2
makes the scaler behave correctly instead of repeatedly starting machines that
cannot help.

### Guard 4: a machine only helps if the bottleneck is the machine

Guards 1–3 all assume the thing in short supply is compute. For a worker that is
**I/O-bound on a rate-limited dependency**, it is not, and every arithmetic above
silently lies.

Segmentation is the case in hand. Its throughput is set by how many tokens per
minute the model providers will accept, and those limits are drawn down from one
shared pool regardless of how many machines are drawing. When the pools are
saturated, a fifth burst node adds **exactly zero** throughput: it boots, receives a
job, and parks waiting on a reservation the other four are consuming. You pay
`~3 min boot + the wait + the idle before scale-in` for nothing, the backlog does
not fall, so the scaler asks for more — and the job is *slower* than it would have
been, because a routing wait that expires returns it to the queue.

So a class with a rate-limited dependency publishes a second number, and the scaler
takes the smaller answer:

```
wanted = min(
  ceil(backlog_seconds / (target_drain_secs × concurrency_per_node)),
  ceil(downstream_slots / concurrency_per_node)
)
```

`downstream_slots` is whatever "how much parallelism will the dependency accept
right now" means for the class — for segmentation, the free capacity across the
eligible provider pools, read straight out of the buckets the router already
maintains to enforce its own rate limits. The number costs nothing extra to compute;
the publisher is already there and already has the data.

**Classes without such a dependency set `downstream_slots` to infinity and the guard
disappears.** But decide it explicitly per class, because the failure mode is
expensive and completely silent: the fleet looks like it is working, every machine
is "busy", and none of them is doing anything.

### Smooth scaling, not 5 → 1 → 3

Left alone, this loop reacts to its own output with a delay: machines requested in
minute 1 are not running in minute 2, so the backlog still looks terrible, so more
are requested — then they all arrive at once and the fleet overshoots and cuts.
Four things prevent it.

**Declare the delay.** `default_instance_warmup` on the ASG, set to the measured
boot-plus-pull time (~300s, ~60s with a warm pool). Machines already launched then
count toward capacity while they boot, so the next evaluation stops re-ordering
them. This single setting removes most of the oscillation.

**Split the range by mechanism.** Step rules are jumpy by nature — crossing a
threshold by a little does the same thing as crossing it by a lot. Proportional
scaling is smooth but cannot start from zero. Use each where it is good:

| Transition | Mechanism |
|---|---|
| 0 → 1 | step rule on raw `backlog_seconds` — the only thing that works from rest |
| 1 → N | **target tracking** on `backlog_seconds ÷ in-service machines` |
| N → 0 | explicit alarm: no queued and no running work for 15 min → `SetDesiredCapacity 0` |

**Be quick to add, slow to remove.** Deliberately lopsided, because the two
mistakes cost differently: slow to add costs latency on real work; slow to remove
costs a few minutes of one machine, billed by the second.

| | Add | Remove |
|---|---|---|
| fires after | 1 reading (60s) | 15 consecutive readings (15 min) |
| step size | +1, +2 | **−1, always** |
| then waits | the warm-up above | 5 min |

Removing one at a time is what stops "drop to zero, backlog reappears, start
four." The fleet walks down instead of falling.

**Smooth the reading itself.** Either publish a 5-minute rolling average alongside
the instantaneous value and alarm on the average, or require 3 readings out of 5
to trip (`datapoints_to_alarm = 3`, `evaluation_periods = 5`). The second is less
code; the first gives both numbers on a dashboard, which is worth having while
thresholds are still being tuned.

### The feedback loop that scale-in creates

This one is specific to this design and will oscillate on its own if ignored.

If correctness rests on the queue re-delivering work when a machine dies mid-job,
then note that **scale-in is a machine dying mid-job.** Remove a busy node and its
20 minutes of work returns to the queue, backlog jumps, and the fleet scales
straight back out — fighting work it created.

Two defences, and both are wanted:

- **Count running work in `backlog_seconds`** (already specified above). The
  number then neither collapses when work is picked up nor spikes when it returns.
- **Do not kill busy machines** — the lifecycle hook. It is tempting to file this
  as a cost optimisation and defer it; it is not one. It is what keeps scale-in
  from manufacturing new backlog, which is why §8 ships it in the same phase as
  the signal rather than after it.

### Losing work on scale-in

Baseline correctness still rests on **re-delivery plus idempotent handlers**: a
node killed mid-job means its job is retried elsewhere. Work is delayed, never
lost.

Note the interaction with the publisher section above. Once the pending-job list
lives in Postgres, SQS's visibility timeout *may* stop being what provides
re-delivery — a lease column (`claimed_by`, `claimed_until`) plus a reaper that
releases expired claims does the same job. That is a small amount of code, but it
*is* code, where before it was a queue setting.

**Pick one per class and say which.** Two mechanisms that both look like they
provide re-delivery is how you get a job running twice: a lease that expires while
the visibility timeout is still being heartbeat-extended hands the work to a second
worker underneath the first, which is precisely what §2.3 exists to prevent.

The Postgres lease is only *required* when the queue cannot be the claim — when
there is no queue, or when the ledger is authoritative for billing. Otherwise
**prefer the visibility timeout plus a heartbeat**: it already exists, it is one
setting rather than a reaper to write and test, and the pending list in Postgres can
remain a read model for the signal rather than the arbiter of who owns the job.
Segmentation takes this route; the lease stays optional per class rather than
implied by §4.

The corresponding knob is `claim_timeout`, and **it bounds the heartbeat interval,
not the job.** A class whose jobs outlive it is normal and expected — that is what a
heartbeat is for. Sizing it against p99 job duration confuses the two and produces
an absurd number for any long-running class.

The **lifecycle hook** on `autoscaling:EC2_INSTANCE_TERMINATING` avoids wasting
that work. Note the mechanism: during `Terminating:Wait` the instance keeps
running and the OS is **not** shut down, so `ExecStop` does not fire on its own. A
small agent has to notice the state — either polling
`describe-auto-scaling-instances` or EventBridge → SSM Run Command.

**Drain to the last durable point, not to the end of the job.** For a class that
checkpoints — segmentation checkpoints every superstep — the hook needs to cover one
checkpoint, which is seconds, not a run that may take an hour. Set the hook's
heartbeat and `stop_grace_period` to the same number and size both from the
checkpoint interval. A class with no checkpoints has no choice but to drain the
whole job, which is a good reason to add checkpoints rather than a good reason for a
two-hour lifecycle timeout.

This also tempers the feedback loop above for such classes: killing a checkpointing
node returns one superstep to the queue, not twenty minutes, so the backlog spike
the hook exists to prevent is an order of magnitude smaller to begin with. The hook
is still wanted — it is just less load-bearing than it is for a class that restarts
from the beginning.

Scale-in protection (the worker marking itself busy so the ASG never picks it)
stays out of scope: it would put Auto Scaling API knowledge into the worker image,
breaking §2. The lifecycle hook achieves the same end with the knowledge on the
host where it belongs.

### If the job mix is lumpy, fix the queue, not the scaler

A 20-second job queued behind a 30-minute one waits 30 minutes, and no scaling
policy repairs that. Two structural options, both better than tuning thresholds:

- **Raise per-node concurrency.** If a job does not saturate a core, one machine
  at concurrency 2 runs the 30 and the 10 together and finishes in 30 minutes —
  same as two machines, half the cost. Only works when jobs are not flat-out
  CPU-bound; four CPU-saturating jobs on four cores each run at quarter speed and
  nothing is gained.
- **Split long and short into separate classes.** Short jobs stay fast, and each
  fleet then scales on a backlog whose jobs are roughly the same size — which is
  also where the estimator is most accurate. §5 makes a new class one map entry
  precisely so this stays cheap.

### What the fleet does not do: make one job faster

Worth stating as a limitation rather than leaving it to be discovered. One message
is one job on one machine, so **per-job latency is completely unaffected by the
fleet.** Burst nodes help only when several jobs are queued at once. If a product
promise is "this document finishes in N minutes" for a single user watching a single
upload, the fleet contributes nothing to it, and guards 2 and 3 will correctly
decline to start a machine in exactly that case.

The escape hatch exists but is a change to the *worker*, not to the fleet: if a
job's internal fan-out is already parallel and idempotent per unit — segmentation's
label-window phase is both, one S3 artifact per window with an existence check
before the call — then those units can become their own queue and their own class,
and adding machines does make one document faster. That is the only design here with
that property.

Do not build it speculatively. Do record which of the two you have chosen, because
"the fleet will fix our latency" is the assumption most likely to be made silently
and most expensive to discover at the end.

### Cold start

Boot + cloud-init + dnf + ECR pull is realistically 2–5 minutes, dominated by the
pull. If a class's images are large, add an `aws_autoscaling_warm_pool` with
`pool_state = "Stopped"` — pre-booted, pre-pulled instances that cost only EBS and
come into service in 30–60s. This is a per-class toggle, off by default; measure
the pull before enabling it.

Spot is the natural fit for burst compute. Use a mixed-instances policy with
`capacity_rebalance`, and treat interruption exactly as scale-in: the visibility
timeout covers it.

---

## 5. Terraform: one module, many classes

Per-class copy-paste across queue + DLQ + launch template + ASG + three alarms is
the thing that makes a second worker class never get built. So the fleet is a
module, and a class is a map entry.

```hcl
# infra/workers.tf
module "worker_fleet" {
  source   = "./modules/worker-fleet"
  for_each = var.worker_classes

  project               = var.project
  class                 = each.key
  image                 = each.value.image
  worker_role           = each.value.worker_role          # §2: which consumer starts
  asg                   = each.value.asg                  # §1: false = baseline only
  instance_type         = each.value.instance_type
  max_size              = each.value.max_size
  concurrency           = each.value.concurrency
  baseline_concurrency  = each.value.baseline_concurrency  # §1; default 1
  baseline_mem_limit    = each.value.baseline_mem_limit    # §1: the real cap
  claim_timeout         = each.value.claim_timeout         # §4: bounds the heartbeat
  downstream_slots      = each.value.downstream_slots      # §4 guard 4; null = none
  target_drain_secs     = each.value.target_drain_secs     # §4: the one real knob
  idle_before_zero      = each.value.idle_before_zero      # §4 guard 3
  instance_warmup       = each.value.instance_warmup       # measured boot + pull
  warm_pool             = each.value.warm_pool

  subnet_id            = aws_default_subnet.default.id
  security_group_id    = aws_security_group.instance.id
  instance_profile     = aws_iam_instance_profile.instance.name
  ami_id               = data.aws_ssm_parameter.al2023.value
  common_setup         = local.common_setup
  assets_bucket        = aws_s3_bucket.assets.bucket
  host_a_ip            = local.host_a_private_ip
  host_b_ip            = local.host_b_private_ip          # §3: state via the service tier
  ecr_registry         = local.ecr_registry
  aws_region           = var.aws_region
  instance_tag_key     = var.instance_tag_key
  instance_tag_value   = var.instance_tag_value
}
```

The module creates, per class: an SQS queue + DLQ (`maxReceiveCount = 3`), and —
**only when `asg = true`** — a launch template rendering
`user_data.host_workers.sh.tftpl`, an ASG (`min 0 / max N / desired 0`,
`default_instance_warmup = instance_warmup`, tagged `Role=worker` and
`WorkerClass=<class>` with `propagate_at_launch`), the scaling policies and alarms
from §4 — the 0→1 step rule, the 1→N target tracking, the N→0 alarm, the two native
backstops and the publisher watchdog — and optionally a warm pool. With
`asg = false` it creates the queue and DLQ and stops; the class exists only as a
Host B compose block.

Registering segmentation is then three entries over one image, and only the first
is a fleet:

```hcl
worker_classes = {
  # Interactive runs. The only lane where a machine converts to throughput.
  segmentation-realtime = {
    image                = "protofast-segmentation"
    worker_role          = "runs"
    asg                  = true
    instance_type        = "m7g.large"   # I/O-bound, memory per in-flight run;
                                         # compute-optimised is the wrong shape
    max_size             = 5
    concurrency          = 4
    baseline_concurrency = 8             # §1: asleep on HTTP, not on CPU
    baseline_mem_limit   = "768m"
    claim_timeout        = 900           # heartbeat backstop, NOT a bound on the run
    downstream_slots     = "provider_pool_headroom"   # §4 guard 4 — load-bearing
    target_drain_secs    = 600
    idle_before_zero     = 900
    instance_warmup      = 300           # measured; ~60 with a warm pool
    warm_pool            = true
  }

  # Bulk imports. The tokens go to provider batch APIs; the worker submits and
  # waits, so a burst machine is pure cost and the depth backstop would fire on
  # every import. Baseline only — see §1 and §4.
  segmentation-bulk = {
    image                = "protofast-segmentation"
    worker_role          = "bulk"
    asg                  = false
    baseline_concurrency = 4
    baseline_mem_limit   = "384m"
    claim_timeout        = 900
  }

  # Delayed polls of in-flight provider batches. Milliseconds of work per message,
  # invisible until its delay expires. Nothing to scale.
  segmentation-batch-poll = {
    image                = "protofast-segmentation"
    worker_role          = "batch-poll"
    asg                  = false
    baseline_concurrency = 2
    baseline_mem_limit   = "256m"
    claim_timeout        = 300
  }
}
```

Three classes, one image, one ECR repo. The `mem_limit`s total under 1.5 GB, which
is §1's whole baseline budget — so segmentation is the only class with a Host B
footprint until Host B grows.

---

## 6. Deploying a worker — and why it cannot reuse the SSM path

`_component-deploy.yml` resolves a single target with `describe-instances` and
fails hard when none is running:

```
[ -n "$id" ] || { echo "no running ... instance"; exit 1; }
```

At desired=0 that aborts the deploy, and with several nodes up `--output text`
returns a tab-separated list that `--instance-ids` will not accept. The existing
path also demands a running peer to resolve `HOST_A_IP`. **None of it survives a
fleet whose normal size is zero.** Worker deploys need their own route.

They get one that reuses everything that does work:

1. **Build + push** — the existing `build` job, unchanged.
2. **Apply on Host B** — SSM `deploy.sh apply worker-<class>=<tag>` against
   `Role=services`, which always exists. This pins `WORKER_<CLASS>_TAG` in
   `versions.env`, recreates the baseline container if the class has one, and runs
   `push_manifest` to publish the manifest to S3.
3. **Refresh the fleet** — `aws autoscaling start-instance-refresh` on the class's
   ASG. A no-op at desired=0, which is the common case; nodes booting later read
   the new tag from the S3 manifest in step 3 of §3.

Host B is the deploy anchor for every class, including classes with no baseline
worker — it is simply the box that is guaranteed to be there to write and publish
the manifest.

**The ordering inside step 2 is load-bearing for any class with migrations.** A
class that owns database schema runs its one-shot migration container as a pre-step
of the apply, and `push_manifest` runs after. So the new tag only becomes visible in
S3 once the schema it expects is in place, and a node booting afterwards can never
run ahead of the database. Get that order backwards and a burst node booting during
a deploy pulls a binary whose migrations have not run — on a tier with no operator
attached and no way to notice. Segmentation's `segmentation-migrations` component
already sits in the right place; keep it there.

### The zombie node

Host B's bootstrap wraps its S3 fetch in `if ... then ... else echo "skipping"`.
On a named instance that is benign — you notice and redeploy. On an ASG node it is
a machine that passes EC2 status checks, runs no container, drains nothing, and
bills you while the queue grows.

So the worker unit must fail loudly. On bootstrap failure, call
`aws autoscaling set-instance-health --health-status Unhealthy` so the ASG replaces
it rather than keeping a silent passenger. This is not optional polish; it is the
difference between a fleet and a leak.

---

## 7. What has to change

| File | Change |
|---|---|
| `infra/modules/worker-fleet/` | **new** — queue, DLQ, launch template, ASG, policies, alarms, optional warm pool |
| `infra/workers.tf` | **new** — `for_each` over `var.worker_classes` |
| `infra/variables.tf` | **new** `worker_classes` map variable |
| `infra/templates/user_data.host_workers.sh.tftpl` | **new** — §3; `${common_setup}` plus seed / fetch / systemd, no EBS, no secret fan-out. Seeds `HOST_B_IP` as well as `HOST_A_IP` |
| `infra/network.tf` | **no change.** §3 routes workers to state through `api`/`auth` on 8080–8083 and OTLP on 4317–4318, both already `self = true`. Publishing 5432/6379 is the fallback, and it drags a Redis password with it |
| `infra/iam.tf` | instance profile gains `autoscaling:SetInstanceHealth`, `cloudwatch:PutMetricData` (the publisher), and `autoscaling:CompleteLifecycleAction` (phase 2). **Not** SQS or S3 for segmentation — its own plan already attaches those to the shared `aws_iam_role.instance`, which burst nodes use. Check before adding a class: the grant may already exist |
| `infra/bootstrap/roles.tf` | deploy role gains `autoscaling:StartInstanceRefresh` + `DescribeAutoScalingGroups` |
| `infra/ecr.tf` | one repo per worker **image** — not per class. Several classes may share one image (§1) |
| `deploy/docker-compose.host-workers.yml` | **new** — the single generic `worker` service (§3), setting `Worker__QueueUrl`, `Worker__Concurrency`, `Worker__Role` |
| `deploy/docker-compose.host-services.yml` | one capped service block per baseline class at its `baseline_concurrency`, plus the backlog publisher (§4) for every class that has a fleet. Baseline-only classes (§1) appear here and nowhere else |
| **jobs schema** | pending/running/done rows with `job_type`, **a size measure**, `started_at`, `elapsed_ms`; the fitted `a`/`b` per type the publisher reads (§4). A `claimed_until` lease and its reaper only for classes that chose the lease over the visibility timeout. Owned per class, shape shared |
| **backlog publisher** | **new** — reads pending + running work, applies the size-based estimator, and emits `backlog_seconds` plus `downstream_slots` (§4 guard 4) once a minute. Prefer a timer inside an always-on service that already reads the class's job state over a new container; for segmentation that is `api` |
| `deploy/deploy.sh` | a `worker)` case in **both** `HOST_ROLE` switches — `host_bringup_sets` and the peer-IP gate. Without it a worker falls through to the `*)` edge default and bootstraps the wrong set. The gate must check `HOST_B_IP` (§3), not the `HOST_A_IP` that `services` checks |
| `.github/workflows/_component-deploy.yml` | a `worker` route: apply on `Role=services`, then `start-instance-refresh`; publish the workers compose file to S3 |
| `docs/01-topology.md`, `docs/07-deployment.md`, `docs/08-infrastructure.md` | document the third role once it exists |

---

## 8. Phasing

The ordering is deliberate: each phase is provable on its own, and the baseline
worker comes **last** rather than first, because it is the thing that would hide
the signals the earlier phases depend on.

**Phase 1 — the tier exists, on native metrics only.** Module, one class, queue +
DLQ, ASG 0–5, worker user_data and systemd unit, the `worker)` cases in
`deploy.sh`, the deploy route, unhealthy-on-bootstrap-failure. Scaling runs on the
AWS-published backstops alone (age / depth step rules), with
`default_instance_warmup`, the fast-add / slow-remove asymmetry and one-at-a-time
scale-in already in place — those are §4's smoothing and they cost nothing to set
correctly from day one. Correctness rests on re-delivery + idempotency. **No
baseline worker yet**: a baseline would hold the message count down and mask
exactly the alarms this phase is trying to prove. Prove zero → N → zero with a
trivial sleep-and-finish worker image before any real workload depends on it.

**Phase 2 — the real signal, and the hook that keeps it honest.** The jobs table,
timing collection paired with a size measure, the fitted estimator, the Host B
publisher, the watchdog alarm, target tracking for 1→N, and the four guards from
§4 — including guard 4 for any class with a rate-limited dependency, which is not
deferrable for such a class because without it the fleet scales into a ceiling it
cannot see. The **lifecycle hook** lands here rather than later, because
scale-in driven by a real backlog number is precisely what creates the feedback
loop in §4 — removing a busy node returns its work to the queue and the fleet
chases work it created. Signal and hook are one change, not two.

**Phase 3 — the baseline worker.** The Host B service block with hard caps at the
class's `baseline_concurrency`. Safe now: a work-time signal does not care who is
draining, so the masking problem that ruled it out of phase 1 no longer applies.
Verify that directly — put a backlog in front of a running baseline and confirm the
fleet still wakes. Baseline-only classes (§1) land here too; they skip phases 1 and
2 entirely, having no ASG and no signal to publish.

**Phase 4 — cost.** Spot with `capacity_rebalance`; warm pool for classes whose
pull time is proven to hurt.

Segmentation becomes a consumer at phase 3 or later, by adding three
`worker_classes` entries over one image and one ECR repo (§5). It does not touch
the module. Note the sequencing against its own plan: the queues and the Host B
baseline are early work there, while the burst tier answers a scaling problem that
only exists once model routing is real — so the ASG belongs with that milestone,
not with the milestone that first creates the queues.

---

## 9. Open questions

Four questions from the first draft are now answered in place and are recorded here
only so the reasoning is not re-run: workers reach state **through the service tier**
(§3, which also settles the SG rules and where the publisher lives); the publisher is
**an always-on service that already reads the class's job state**, not a new
container (§4); the signal is a **size-based estimator**, not a per-type average
(§4); and **SQS keeps its place** — the visibility timeout plus a heartbeat stays the
default claim, with the Postgres lease optional per class (§4, "Losing work").

Still open:

- **The size measure per class, and how well the fit holds.** §4 replaced the
  per-type average with `a + b × size`, which is only better if `size` actually
  predicts cost. It has to be measured per class — and if the residuals are wide,
  the fix is the same one the average needed: split the type until each one
  describes a real job. Every number in §4 and §5 is a placeholder until this is
  measured.
- **What `downstream_slots` means per class** (§4 guard 4). For segmentation it is
  provider pool headroom, which the router already tracks. For a class with a
  different bottleneck — a database connection pool, a third-party API, a licence
  count — it is a different number, and a class with none sets it to infinity. The
  guard is cheap; deciding it is the work.
- **Does any class need per-job parallelism?** §4 records that the fleet makes many
  jobs faster and one job no faster at all. If a product promise turns out to be
  about a single job's latency, the answer is a second class over the job's internal
  fan-out — a change to that worker, not to the module. Decide it before a promise is
  made, not after.
- **`target_drain_secs` per class** — the one knob §4 reduced everything else to.
  It is a product decision ("how late may this work be?"), not an infra one, and
  it should be answered by whoever owns the workload.
- **Image size.** Decides whether the warm pool is worth its complexity, and
  `instance_warmup` cannot be set honestly without it.
- **Does any class need a GPU?** If so it needs its own AMI, and `common_setup`
  stops being shared.
