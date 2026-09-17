# segmentation

Turns a document in any condition — clean Markdown, a PDF conversion full of layout noise, a flat
transcript — into a paragraph list with stable identifiers and a nested section tree. The design is
[`docs/theplot-segmentation-plan.md`](../../docs/theplot-segmentation-plan.md); this file is the
map of what is here and how to work on it.

## The one idea

**Code runs the pipeline; models make judgement calls.** Parsing, cleaning, validation, freezing,
routing and retries are deterministic C#. A model is asked only to label lines, infer hierarchy and
augment — and it answers with identifiers, never with text. Paragraph text is joined from the
cleaned source by code, so `text-integrity` can be a hard gate rather than a hope: if the
concatenated output does not equal the cleaned input character for character, the run does not
freeze.

Two consequences run through everything below. A document the source already describes well costs
nothing — triage skips labelling and the tree is built from the trusted headings, so a clean
Markdown upload finishes in seconds with no provider call at all. And every phase leaves a file in
S3, so a run that went wrong is diagnosed by reading objects rather than by reproducing it.

## Projects

| Project | What it holds |
|---|---|
| `Core` | Records, ids, ingest, cleaning, triage, windowing, assembly, the tree, the validation checks and the evaluation metrics. **Pure** — no I/O, no clock, no configuration beyond options objects. This is where the interesting logic and most of the tests are. |
| `Data` | EF Core context, entities and migrations for the `segmentation` database. Mirrors `ProtoFast.Auth.Data`. |
| `SchemaMigrations` | The one-shot migration runner. Neither service migrates on boot. |
| `Storage` | The S3 artifact store, the SQS clients, and the MAF checkpoint store. |
| `Routing` | The model registry, the Redis budget ledger, the four provider adapters and `RoutingChatClient` — the single door to every provider. |
| `Pipeline` | The MAF executors and the workflow graph, the agents, and the embedded prompt assets. |
| `Worker` | The host: two SQS consumers, the workflow host, and a gRPC health endpoint. Publishes no port. |
| `tools/Cli` (`segctl`) | Runs the deterministic phases over a local file and scores them against the gold set. No AWS, no database, no keys. |

The gRPC surface lives in `services/api` (`Protos/segmentation.proto` + `SegmentationService`), not
here: it rides the `/api/` route and the internal JWT that already exist, so the edge does not
change. `api` deliberately references only `Core`, `Data` and `Storage` — without the routing or
pipeline projects in its container there is no seam through which it could call a provider.

## Working on it

```bash
# The deterministic pipeline over a local file. Writes the same artifact names a real run does.
dotnet run --project tools/ProtoFast.Segmentation.Cli -- segment --in paper.md --out ./out

# Every deterministic check, with a non-zero exit if any fails.
dotnet run --project tools/ProtoFast.Segmentation.Cli -- checks --in paper.md

# The metrics of plan §26, per condition bucket.
dotnet run --project tools/ProtoFast.Segmentation.Cli -- evaluate --gold tests/gold-dev
```

`aspire run` from the repo root starts the whole feature: LocalStack (S3 + SQS, with the bucket and
queues created on start), the `segmentation` database and its migrations, the worker, and ThePlot on
`https://localhost:20002`. Provider keys come from user secrets and are optional —

```bash
cd apphost && dotnet user-secrets set Parameters:anthropic-api-key sk-...
```

— because a developer with no keys still gets a working stack: clean fixtures run end to end
without one.

## Tests

| Suite | What it covers | Needs |
|---|---|---|
| `UnitTests` | The checks, the cleaning rules, the window planner, the metrics, and the CI evaluation gate over `tests/gold-dev`. | nothing |
| `ContractTests` | The provider adapters against recorded responses, including 429s and malformed JSON; the prompt assets; model-output parsing. | nothing |
| `IntegrationTests` | The gRPC surface's ownership and idempotency rules, and the whole workflow end to end over an in-memory store. | nothing |

All three run offline in under two seconds. That is deliberate: a gate people skip is not a gate.

## Things that will bite you

- **Executor ids are written into checkpoints.** Changing one in `ExecutorIds` strands every
  in-flight run across a deploy, because the resumed workflow looks for an executor that no longer
  exists.
- **Prompt assets are embedded resources with pinned `LogicalName`s.** The default resource name
  mangles hyphens in directory names; the csproj pins the name to the path so the string in
  `PromptAssets.Read` is the file on disk. `ContractTests` fails the build if an asset goes missing.
- **Changing a prompt changes `PromptVersion`**, which is part of the qualification lookup key — so
  every model's qualification for that role stops matching until it is re-evaluated. That is the
  intended behaviour, not an accident.
- **Object lock cannot be turned off** once the bucket exists. Read the note in
  `infra/segmentation.tf` before the first apply.
