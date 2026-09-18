# ThePlot — Universal Document Ingest (MarkItDown Conversion)

**Extends:** [`theplot-segmentation-plan.md`](theplot-segmentation-plan.md) — §5 (requirements), §9.2 (phase 0), §16 (storage), §17 (gRPC), §18.3 (upload), §19 (AWS), §20 (config)
**New component:** `services/conversion` — a Python conversion sidecar (`protofast-conversion`) on Host B
**Status:** Draft for review

> MarkItDown's converter set, its optional-dependency extras and the AWS SDK's presigned-POST
> helper all move between versions. Code in this document is an illustrative sketch against the
> conventions already in this repo; confirm every signature and every extra against the versions
> actually pinned before implementing. Points that need confirming are called out as **[verify]**.

---

## Table of contents

1. [Summary](#1-summary)
2. [What this changes](#2-what-this-changes)
3. [Goals and non-goals](#3-goals-and-non-goals)
4. [Requirements](#4-requirements)
5. [Acceptance criteria](#5-acceptance-criteria)
6. [Accepted formats](#6-accepted-formats)
7. [The 10 MiB limit, enforced by S3](#7-the-10-mib-limit-enforced-by-s3)
8. [Where conversion runs](#8-where-conversion-runs)
9. [The conversion contract](#9-the-conversion-contract)
10. [Inside the converter](#10-inside-the-converter)
11. [Layout extraction](#11-layout-extraction)
12. [OCR policy](#12-ocr-policy)
13. [Storage and keys](#13-storage-and-keys)
14. [The gRPC surface](#14-the-grpc-surface)
15. [ThePlot client](#15-theplot-client)
16. [Changes to existing code](#16-changes-to-existing-code)
17. [Configuration contract](#17-configuration-contract)
18. [AWS infrastructure](#18-aws-infrastructure)
19. [Local development](#19-local-development)
20. [Deployment](#20-deployment)
21. [Security and data governance](#21-security-and-data-governance)
22. [Observability](#22-observability)
23. [Failure modes](#23-failure-modes)
24. [Testing](#24-testing)
25. [Capacity](#25-capacity)
26. [Milestones](#26-milestones)
27. [Open questions](#27-open-questions)
- [Appendix A: format matrix](#appendix-a-format-matrix)
- [Appendix B: `conversion.json`](#appendix-b-conversionjson)
- [Appendix C: proto diff](#appendix-c-proto-diff)

---

## 1. Summary

Today ThePlot accepts Markdown and plain text only ([`upload.ts`](../clients/theplot/src/app/pages/upload/upload.ts) pins
`accept=".md,.markdown,.txt"`), with an optional `.layout.json` the user has to have produced
themselves with some upstream converter. The segmentation plan made that explicit: "PDF-to-Markdown
conversion itself" is a §3 non-goal, and F1 says an upstream component supplies Markdown plus
layout.

This plan makes ProtoFast that upstream component. After it:

1. A user drops **any document MarkItDown can read** — PDF, Word, PowerPoint, Excel, HTML, EPUB,
   Outlook mail, a notebook, a scan — and never has to produce Markdown or layout by hand.
2. The upload is capped at **10 MiB, and the cap is enforced by S3 itself**, not by a client-side
   check the browser can skip. Oversized bytes are refused by the bucket at the edge of the
   platform and never reach a queue, a worker or a bill.
3. **MarkItDown converts on our own hardware**, writes `…/{uploadId}.md` and, where the format
   carries geometry, `…/{uploadId}.layout.json` to the same S3 keys the pipeline already reads —
   then segmentation proceeds exactly as it does today.
4. **No LLM and no GPU is involved in conversion.** MarkItDown's image-captioning path and Azure
   Document Intelligence are both left unconfigured. Where a scan has no text layer, CPU Tesseract
   supplies one.

The last point is not a cost preference, it is what keeps the feature inside the sensitivity model
already in the plan (§24): conversion never sends a byte of a `RESTRICTED` document to a third
party, because conversion never leaves Host B.

## 2. What this changes

| Area | Change |
|---|---|
| **New component** | `services/conversion` — Python 3.12 + FastAPI + MarkItDown + Tesseract, image `protofast-conversion`, Host B, no published port |
| **`api`** | `CreateUpload` returns a **presigned POST** (policy-enforced size) instead of a presigned PUT; new `ListSourceFormats` RPC; `MaxUploadBytes` 64 MiB → 10 MiB |
| **`segmentation` worker** | `IngestExecutor` gains a conversion step in front of its existing read: non-Markdown sources are handed to the converter, which writes the Markdown and layout the executor then reads |
| **Data** | `uploads` table gains `SourceKey`, `MediaType`, `RequiresConversion`; one EF migration |
| **Storage** | New key `uploads/{sub}/{uploadId}{ext}` for the original; the existing `.md` / `.layout.json` keys become *converter output* rather than *user output* |
| **New AWS** | One ECR repo (`protofast-conversion`); bucket CORS gains `POST` |
| **ThePlot** | Accept list and size cap come from the server; upload switches from `PUT` to multipart `POST`; the phase-0 blurb mentions conversion |
| **Docs** | This plan; [layer 09](09-reference.md) gains the new variables once it ships |

Nothing about auth, the edge, the phase ladder's numbering, the tree rules or the model routing
changes. The pipeline downstream of phase 0 does not know conversion happened.

## 3. Goals and non-goals

### Goals

- Accept every format MarkItDown supports that is a *document* and can be converted locally.
- Refuse anything over 10 MiB **at S3**, with an error the user can act on.
- Produce layout metadata automatically wherever the source format carries geometry, so the
  layout-aware half of the pipeline (`MarkdownExtractor`, `LayoutJoiner`, the `HasLayout`
  statistic) keeps working for formats where it did not before.
- Keep conversion deterministic, local, CPU-only and reproducible: the same bytes convert to the
  same Markdown, and the run's content hashes stay meaningful.
- Reuse the existing run lifecycle. No new queue, no new status surface, no second thing to watch.

### Non-goals

- **LLM anything.** No image captioning, no LLM cleanup of OCR output, no model-assisted table
  reconstruction. MarkItDown is constructed without `llm_client`.
- **GPU anything.** No CUDA base image, no GPU OCR engine, no local Whisper.
- **Third-party document AI.** Azure Document Intelligence is supported by MarkItDown and is
  deliberately not configured; it would send document bytes off the box.
- **Audio and video.** MarkItDown's transcription path routes audio to an external speech API by
  default; both the format family and the path are excluded (§6).
- **Archives (`.zip`) and URL sources (YouTube, RSS).** A 10 MiB archive can expand to gigabytes,
  and a URL source is an SSRF surface. Both are deferred with their guards named in §27.
- **Faithful table or figure structure.** Tables convert to Markdown tables on a best-effort basis
  and remain opaque blocks to segmentation, exactly as the parent plan says (§3).
- **Editing the conversion.** The user gets the converted Markdown as a run artifact and can
  re-upload a corrected version; there is no in-app Markdown editor in v1.

## 4. Requirements

### 4.1 Functional

| ID | Requirement |
|---|---|
| C1 | Accept any source format on the §6 allowlist, identified by extension **and** media type. |
| C2 | Refuse an upload over 10 MiB such that the refusal comes from S3, not from the client. |
| C3 | Convert the source to Markdown with MarkItDown, without any LLM or remote service. |
| C4 | Emit `.layout.json` for every format that carries page geometry (PDF, images); omit it — rather than fabricate it — for formats that do not. |
| C5 | Apply CPU OCR (Tesseract) when, and only when, a PDF page or an image has no usable text layer. |
| C6 | Write the Markdown, the layout and a conversion report to S3 before segmentation reads them. |
| C7 | A Markdown or plain-text upload bypasses the converter entirely (the source *is* the output). |
| C8 | Conversion failure fails the run at phase 0 with a message naming the format and the reason. |
| C9 | The accepted-format list and the size cap are served by `api`, so the client cannot drift from them. |
| C10 | Re-running a run from phase 0 reuses an existing conversion rather than repeating it. |

### 4.2 Non-functional

| ID | Requirement |
|---|---|
| N1 | Conversion of a 10 MiB text-layer PDF completes in under 60 s on a `t4g.medium` vCPU pair. |
| N2 | The converter holds at most one source in memory at a time and is bounded by an explicit memory cap. |
| N3 | The converter has no network egress except the S3 endpoint. |
| N4 | Every third-party binary the converter shells out to (Tesseract, Ghostscript via OCRmyPDF, ExifTool) is pinned by the image and runs with a wall-clock timeout. |
| N5 | Conversion is idempotent on the upload id: the same source converts to the same object, and a re-delivered message is free. |

## 5. Acceptance criteria

1. Uploading a 12 MiB PDF shows "That file is larger than 10 MB" and **no object exists** under
   `uploads/` afterwards — verified against the bucket, not against the UI.
2. Uploading a 3 MiB text-layer PDF produces a run whose `00_source.md` is non-empty, whose
   `.layout.json` has one entry per extracted line, and whose `HasLayout` statistic is `true`.
3. Uploading a scanned (image-only) PDF produces Markdown with recognisable text and a
   `conversion.json` whose `ocr.applied` is `true` and whose `ocr.engine` is `tesseract`.
4. Uploading a `.docx`, `.pptx`, `.xlsx`, `.html` and `.epub` each produce a run that reaches
   `Publish`, with `HasLayout` `false` and no `.layout.json` object.
5. Uploading a `.md` produces a run in which the converter container records **zero** requests.
6. The converter image contains no CUDA runtime, and `pip list` inside it shows no
   `openai`/`anthropic`/`azure-ai-*` package. Asserted in CI, not by inspection.
7. `ListSourceFormats` and the client's `accept` attribute agree, asserted by a client test that
   builds the attribute from the RPC's reply.
8. Cutting the converter's network egress to everything but S3 leaves every case above passing.

## 6. Accepted formats

The allowlist lives in one place in C# — `ProtoFast.Segmentation.Core.Ingest.SourceFormats` — and
reaches `api` (validation), the converter (dispatch assertion) and the browser (the `accept`
attribute, via `ListSourceFormats`) from there. See [Appendix A](#appendix-a-format-matrix) for the
per-format converter and dependency.

| Family | Extensions | Layout | OCR | Notes |
|---|---|---|---|---|
| Markdown / text | `.md` `.markdown` `.txt` | no | no | Passthrough; the converter is never called |
| PDF | `.pdf` | **yes** | when no text layer | The layout-bearing format the pipeline was designed around |
| Word | `.docx` | no | no | `.doc` (legacy binary) rejected — unsupported upstream |
| PowerPoint | `.pptx` | no | no | Slide order is structure enough; no geometry emitted |
| Excel | `.xlsx` `.xls` | no | no | Sheets become Markdown tables |
| Web | `.html` `.htm` | no | no | Local file only; no URL fetching |
| Data | `.csv` `.tsv` `.json` `.xml` | no | no | Structure-only conversion |
| Books | `.epub` | no | no | |
| Mail | `.msg` | no | no | Outlook item; attachments are not recursed in v1 |
| Notebooks | `.ipynb` | no | no | |
| Images | `.png` `.jpg` `.jpeg` `.tif` `.tiff` `.bmp` | **yes** | always | Text comes from OCR; layout comes from the OCR boxes |

**Rejected, with the reason returned to the user:** audio and video (`.mp3`, `.wav`, `.m4a`,
`.mp4`, …) — transcription is either a remote API or a local model, and both are out of scope;
`.zip` — decompression-bomb surface (§27); URL sources — SSRF surface; anything not on the list —
"ThePlot cannot read *.xyz files yet".

> Rejection happens twice: `CreateUpload` refuses to mint a URL for an unlisted extension or media
> type, and the converter asserts the format again before dispatching. The second check is not
> redundant — the presigned POST pins the key and its `Content-Type`, but an attacker with a valid
> URL can still put *different bytes* under an allowed extension.

## 7. The 10 MiB limit, enforced by S3

**10 MiB = 10,485,760 bytes.** This is the size of the *source* upload, before conversion.

### 7.1 Why the current design cannot enforce it

`CreateUpload` mints a presigned **PUT** ([`S3ArtifactStore.PresignPut`](../services/segmentation/src/ProtoFast.Segmentation.Storage/S3ArtifactStore.cs:197)),
and the options file already admits the gap:

> "The presigned URL itself cannot enforce a size, so the limit is asserted here and again at
> ingest — a browser that ignores it wastes its own bandwidth and the object expires in seven days."
> — [`SegmentationApiOptions.cs:24`](../services/api/src/ProtoFast.Api/Segmentation/SegmentationApiOptions.cs)

That is true of PUT. A SigV4 PUT signature covers the key, the verb, the `Content-Type` and the
expiry — never the body length. `request.SizeBytes` is a number the browser typed; a client that
sends 500 MiB against a URL signed for "1 KB" succeeds.

### 7.2 Presigned POST with `content-length-range`

A presigned **POST** signs a *policy document*, and the policy can carry a
`content-length-range` condition. S3 evaluates it against the actual bytes and rejects the upload
with `400 EntityTooLarge`. That is the enforcement point the requirement asks for.

```csharp
// ProtoFast.Segmentation.Storage — new method on IPresignedUrlFactory.
public PresignedPost PresignPost(string key, string contentType, long maxBytes, TimeSpan? ttl = null)
{
    var expiration = DateTime.UtcNow.Add(ttl ?? _options.UploadUrlTtl);

    // Conditions are exact-match unless stated otherwise: the browser may choose neither the key
    // it writes to nor the type it declares, which is what stops one user's presigned POST from
    // being aimed at another user's prefix.
    var policy = $$"""
    {
      "expiration": "{{expiration:yyyy-MM-ddTHH:mm:ssZ}}",
      "conditions": [
        {"bucket": "{{_options.Bucket}}"},
        {"key": "{{key}}"},
        {"Content-Type": "{{contentType}}"},
        {"success_action_status": "201"},
        ["content-length-range", 1, {{maxBytes}}]
      ]
    }
    """;

    var signed = Amazon.S3.Util.S3PostUploadSignedPolicy.GetSignedPolicy(policy, _credentials, _options.Region);

    // The browser posts these verbatim as form fields, in this order, with the file LAST — S3
    // stops reading at the file part, so a field after it is not seen.
    var fields = new Dictionary<string, string>
    {
        ["key"] = key,
        ["Content-Type"] = contentType,
        ["success_action_status"] = "201",
        ["x-amz-algorithm"] = signed.Algorithm,
        ["x-amz-credential"] = signed.Credential,
        ["x-amz-date"] = signed.Date,
        ["policy"] = signed.Policy,
        ["x-amz-signature"] = signed.Signature,
    };

    if (!string.IsNullOrEmpty(signed.SecurityToken))
    {
        fields["x-amz-security-token"] = signed.SecurityToken;   // instance-role creds are session creds
    }

    return new PresignedPost(PostUrl: $"{BucketEndpoint()}", Fields: fields, ExpiresAt: expiration);
}
```

**[verify]** `Amazon.S3.Util.S3PostUploadSignedPolicy.GetSignedPolicy(policyJson, credentials, region)`
exists in the pinned `AWSSDK.S3` (4.0.103.3 at time of writing) and exposes `Policy`, `Signature`,
`Algorithm`, `Credential`, `Date`, `SecurityToken`. Confirm whether it *injects* the `x-amz-*`
conditions into the policy itself (the `AddConditionsToPolicy` helper suggests it does) — if it
does not, add them to the `conditions` array above or every POST fails with
`Policy Condition failed`. If the helper turns out to be unusable, signing a POST policy by hand is
about forty lines: base64 the policy JSON, then SigV4-HMAC it with the date/region/`s3`/
`aws4_request` key chain.

### 7.3 Enforcement in depth

| # | Where | What it catches | Authoritative? |
|---|---|---|---|
| 1 | Browser, before `CreateUpload` | The common case, instantly, with a friendly message | no |
| 2 | `CreateUpload`, against `MaxUploadBytes` | A declared size over the limit; avoids minting a doomed URL | no |
| 3 | **S3 POST policy** | Actual bytes over the limit, regardless of what the client said | **yes** |
| 4 | Converter, reading `ContentLength` before `GetObject` | A stale object, or a limit lowered after the URL was minted | yes, as a backstop |

Check 4 matters more than it looks: the converter is the thing that loads the bytes into memory, so
it is the last place a size assumption can be cheaply wrong.

### 7.4 Surfacing the refusal

S3 answers a too-large POST with `400` and an XML body containing
`<Code>EntityTooLarge</Code>`. The client reads the status and the code and maps it:

| S3 code | Message shown |
|---|---|
| `EntityTooLarge` | "That file is larger than 10 MB. Try splitting it or exporting a smaller version." |
| `AccessDenied` + expired policy | "The upload window expired. Try again." |
| anything else | "The upload failed. Try again." |

## 8. Where conversion runs

**Decision: conversion is a stateless sidecar called from phase 0 (Ingest) of the existing
pipeline.**

The converter is a Python HTTP service on Host B with one endpoint. It reads the source from S3 and
writes the Markdown and layout back to S3 itself — the bytes never cross the wire between it and
the .NET worker. It owns no database, no queue and no state. The .NET
[`IngestExecutor`](../services/segmentation/src/ProtoFast.Segmentation.Pipeline/Executors/DeterministicExecutors.cs:22)
calls it, and everything else about orchestration — the phase gate, the idempotency key, the
journal, run events, resume-after-restart, the SQS visibility heartbeat — is machinery that already
exists and does not have to be built again in a second language.

```
browser ──POST(policy)──▶ S3  uploads/{sub}/{id}.pdf
   │
   └──SubmitRun──▶ api ──SQS──▶ segmentation worker
                                     │  phase 0: Ingest
                                     │
                                     ├─ POST /convert {sourceKey, …} ──▶ conversion (Python)
                                     │                                      │ GET  source
                                     │                                      │ MarkItDown (+OCR)
                                     │                                      │ PUT  {id}.md
                                     │                                      │ PUT  {id}.layout.json
                                     │◀── {markdownKey, layoutKey, pages} ──┘
                                     │
                                     ├─ read .md + .layout.json  (unchanged code)
                                     └─ phases 1…12  (unchanged)
```

### 8.1 Alternatives considered

| Option | Why not |
|---|---|
| **Conversion as a new phase 0, renumbering Ingest→1 and everything after** | Cleanest on paper, and the artifact filenames (`00_lines.jsonl` …) would still read in pipeline order. But the indices are pinned in four places on purpose — `ArtifactKeys.Phase`, the `PipelinePhase` enum, `RerunFrom`'s argument, and `PHASE_LABELS` in [`phases.ts`](../clients/theplot/src/app/segmentation/phases.ts) whose comment says exactly this — plus the gold recordings. A large mechanical change for a cosmetic gain |
| **A separate Python worker on its own SQS queue, converting before the run exists** | This is the literal reading of "convert, upload, then start the run", and it isolates conversion cost cleanly. It costs: a fourth queue, a second DB writer (or a completion message and a second consumer to apply it), an upload state machine, a `GetUpload` RPC and a client that polls between upload and submit. All to reproduce a status surface `WatchRun` already provides. Worth revisiting if conversion ever needs to be independently scalable |
| **Conversion in-process in .NET (a Markdown converter per format)** | Reimplements MarkItDown, badly, in a language with worse coverage of these formats |
| **`Python.NET` / IronPython in the worker** | Couples a GIL and a native toolchain to the worker's lifecycle; no isolation, no separate memory cap, no separate image |

If the separate-worker shape is preferred after review, the converter service body is unchanged —
only its caller moves. That is the point of keeping it stateless.

## 9. The conversion contract

One endpoint, plus health. FastAPI + Uvicorn, HTTP/1.1, internal to Host B's compose network.

```
POST /convert
{
  "uploadId":      "upl_01J…",
  "sourceKey":     "uploads/9f2…/upl_01J….pdf",
  "markdownKey":   "uploads/9f2…/upl_01J….md",
  "layoutKey":     "uploads/9f2…/upl_01J….layout.json",
  "reportKey":     "uploads/9f2…/upl_01J….conversion.json",
  "mediaType":     "application/pdf",
  "fileName":      "annual-report.pdf",
  "ocr":           { "enabled": true, "languages": ["eng"] },
  "traceparent":   "00-…-…-01"
}

200 OK
{
  "markdownKey": "uploads/9f2…/upl_01J….md",
  "layoutKey":   "uploads/9f2…/upl_01J….layout.json",   // "" when the format carries no geometry
  "markdownBytes": 184213,
  "pages":  42,
  "producer": "markitdown/0.1.x+pdfplumber",
  "ocr": { "applied": true, "engine": "tesseract", "pagesOcred": 42, "meanConfidence": 87.4 },
  "warnings": ["3 pages produced no text after OCR"],
  "durationMs": 41230
}

422  { "code": "unsupported_format",  "message": "…" }     // permanent — do not retry
413  { "code": "source_too_large",    "message": "…" }     // permanent
504  { "code": "conversion_timeout",  "message": "…" }     // transient — retry once
500  { "code": "conversion_failed",   "message": "…", "detail": "…" }
```

The split between permanent (4xx) and transient (5xx) is what the caller uses to decide between
failing the run immediately and letting SQS redeliver. A malformed PDF must not consume five
delivery attempts and land in the DLQ.

**Idempotency.** The converter checks whether `markdownKey` already exists with a matching
`x-amz-meta-pf-source-hash` and, if so, returns the existing result without converting. This is the
same trick `IArtifactStore.FindExistingAsync` plays for phase artifacts, and it is what satisfies
C10 and N5.

### 9.1 Caller side

```csharp
// Pipeline/Ingest/DocumentConverter.cs — the worker's half of the contract.
public sealed class DocumentConverter(HttpClient http, IOptions<ConversionOptions> options, ILogger<DocumentConverter> log)
{
    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/convert", request, ct);

        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge)
        {
            // Permanent. Fail the phase now rather than let SQS spend four more attempts on a
            // document that will never convert.
            var problem = await response.Content.ReadFromJsonAsync<ConversionProblem>(ct);
            throw new PipelineFailureException(PipelinePhase.Ingest, problem?.Message ?? "unconvertible", permanent: true);
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConversionResult>(ct))!;
    }
}
```

Registered with a `HttpClient` timeout of `Seg_Conversion__Timeout` (default 10 min, above the
converter's own 8 min budget so the converter's structured error wins the race) and a **single**
retry on 5xx. The SQS visibility heartbeat already in the worker keeps the message invisible across
a long conversion; `VisibilityTimeoutSeconds` stays 900.

## 10. Inside the converter

```
services/conversion/
├── Dockerfile
├── pyproject.toml            # pinned, with a lockfile; no extras beyond the list below
├── src/conversion/
│   ├── main.py               # FastAPI app: /convert, /health
│   ├── formats.py            # media type → handler; mirrors SourceFormats.cs (asserted in tests)
│   ├── convert.py            # MarkItDown invocation + passthrough
│   ├── pdf.py                # text-layer probe, OCR decision, layout extraction
│   ├── images.py             # Tesseract OCR + layout from OCR boxes
│   ├── layout.py             # RawLayoutLine emission (the schema in LayoutInput.cs)
│   ├── storage.py            # boto3 S3 get/put, hashes, metadata stamps
│   └── limits.py             # size, page, timeout and memory guards
└── tests/
```

```python
# convert.py — the whole point of this file is the arguments that are NOT passed.
from markitdown import MarkItDown

_md = MarkItDown(
    enable_plugins=False,   # third-party plugins are not vetted and are not installed
    # llm_client / llm_model: never set. Passing them is what turns on image captioning.
    # docintel_endpoint:      never set. Azure Document Intelligence would ship bytes off-box.
)

def to_markdown(path: str, media_type: str) -> str:
    return _md.convert(path, file_extension=_ext_for(media_type)).text_content
```

**[verify]** the constructor keyword names and `convert()`'s return attribute against the pinned
MarkItDown; both have changed across releases.

**Installed extras:** the document ones only — `[pdf]`, `[docx]`, `[pptx]`, `[xlsx]`, `[xls]`,
`[outlook]`. **Not installed:** `[audio-transcription]`, `[youtube-transcription]`,
`[az-doc-intel]`. Installing `markitdown[all]` would pull the last three in and is the single
easiest way to violate §3 by accident, so the dependency list is explicit and CI asserts the
absence of their packages (acceptance criterion 6).

**System binaries in the image:** `tesseract-ocr` + `tesseract-ocr-eng`, `ocrmypdf` (which brings
Ghostscript and `pngquant`), `exiftool`, `poppler-utils`. All CPU, all Debian-packaged, all pinned
by the base image tag. No CUDA, no `torch`.

**Guards** (`limits.py`): source ≤ 10 MiB (re-checked against S3's `ContentLength`); pages ≤ 200 for
OCR and ≤ 2,000 otherwise; output Markdown ≤ 25 MiB; per-conversion wall clock ≤ 8 min; each
subprocess gets its own timeout and is killed with its process group. The container runs with
`mem_limit: 1g` and one worker process, so a runaway conversion is OOM-killed rather than taking the
box's page cache with it.

## 11. Layout extraction

MarkItDown produces Markdown. It does not produce geometry — and geometry is what
`MarkdownExtractor.Extract(markdown, layout)` and `LayoutJoiner` use to tell a heading from a bold
line. So layout is ours to extract, alongside the conversion, in the schema
[`LayoutInput.cs`](../services/segmentation/src/ProtoFast.Segmentation.Core/Ingest/LayoutInput.cs)
already defines: `page`, `text`, `x`, `y`, `width`, `height`, `pageWidth`, `pageHeight`,
`fontSize`, `bold`, `italic`, `column`.

| Source | Library | What we get |
|---|---|---|
| PDF (text layer) | **pdfplumber** (MIT, on `pdfminer.six` — the same parser MarkItDown itself uses for PDFs) | Per-character font name, size and bbox, grouped into lines; `bold`/`italic` inferred from the font name; `pageWidth`/`pageHeight` from the page box |
| PDF (OCR'd) | pdfplumber over the OCRmyPDF output | Identical, because OCRmyPDF writes a real text layer into the PDF |
| Images | `pytesseract.image_to_data` | One line per `line_num` group, with `left/top/width/height` and a confidence; `fontSize` approximated from line height |
| Everything else | — | **No layout file.** `with_layout` is false, `HasLayout` is false, and the pipeline uses its no-layout path exactly as it does for a hand-written Markdown upload today |

> **Not faking it is the design.** A `.docx` has no pages until something renders it, and a `.pptx`
> has slide coordinates that mean nothing to a prose segmenter. Emitting a synthetic layout with
> made-up font sizes would feed `LayoutJoiner` confident nonsense, which is worse for the pipeline
> than an honest absence it already handles.

**PyMuPDF is deliberately not used** despite being the better geometry library: it is AGPL, and
linking it into a product image would put the platform's licensing in play. pdfplumber is MIT and
sufficient. Note this in review — if a commercial PyMuPDF licence is acquired the choice is worth
revisiting for speed.

**Alignment.** `LayoutJoiner` aligns layout lines to Markdown lines by text. Because both sides now
come from the same extraction pass over the same PDF, alignment is far more reliable than it was
with a user-supplied file from an unknown converter. The layout writer emits lines in reading
order (page, then top, then left) to keep that alignment cheap.

## 12. OCR policy

> **The rule: no LLM, no GPU. Tesseract is fine because it is neither.**

### 12.1 When OCR runs

Never by default, and never on a document that does not need it:

1. Extract text with pdfplumber. Compute characters per page.
2. If **≥ 30 % of pages** yield **< 100 characters**, the document is treated as scanned.
3. Run `ocrmypdf --skip-text --output-type pdf --optimize 0 --language <langs>` over the source.
   `--skip-text` leaves pages that already have a text layer untouched, so a mixed document is
   OCR'd only where it is blank.
4. Re-extract text and layout from the OCR'd PDF and hand *that* to MarkItDown.

Images always take the OCR path — an image has no text layer by definition, and MarkItDown's own
image handler returns EXIF metadata plus (with an LLM, which we do not have) a caption. Ours
returns the recognised text.

### 12.2 What is excluded, and why it matters here

| Excluded | Why |
|---|---|
| MarkItDown's `llm_client` image captioning | An LLM call per image, to a third party, on document content |
| Azure Document Intelligence (`docintel_endpoint`) | Remote document AI; bytes leave the box; per-page cost |
| Local Whisper / GPU OCR (PaddleOCR-GPU, TrOCR) | GPU and/or a neural model; explicitly out of scope |
| `speech_recognition`'s default backend | Posts audio to a Google endpoint |

The sensitivity consequence is the useful one: because conversion is entirely local, a `RESTRICTED`
document can be converted without any of the provider-routing rules in the parent plan's §14
applying. Conversion sits *before* the point at which sensitivity starts constraining what may
happen to the text.

### 12.3 Quality

OCR output is noisy, and the pipeline is built for exactly that: `DocumentCleaner` removes running
heads and page numbers, hyphenation rejoining is phase 1, and the triage phase routes degraded
regions to a model for labelling (never for rewriting). The conversion report records mean OCR
confidence so a run on a bad scan is explainable after the fact; a mean below
`CONVERSION_OCR_MIN_CONFIDENCE` (default 60) adds a warning to the run's events rather than failing
it — the user may well want the result anyway.

## 13. Storage and keys

```
uploads/{ownerSubject}/{uploadId}.pdf            ← the browser's POST lands here (NEW)
uploads/{ownerSubject}/{uploadId}.md             ← converter output (was: the browser's upload)
uploads/{ownerSubject}/{uploadId}.layout.json    ← converter output (was: the browser's upload)
uploads/{ownerSubject}/{uploadId}.conversion.json← the report (NEW)

runs/{runId}/00_source.md                        ← unchanged: phase 0 copies the Markdown here
runs/{runId}/00_source_layout.json               ← NEW: the layout, copied for the same reason
runs/{runId}/00_conversion.json                  ← NEW: the report, copied under the run prefix
```

Keeping the converter's output on the **existing** `.md` / `.layout.json` keys is what makes this
plan small: `IngestExecutor`'s reads, the `uploads/` lifecycle rule (7 days), the IAM `s3:prefix`
condition and `SubmitRun`'s existence check all keep working untouched.

New entries in [`ArtifactKeys`](../services/segmentation/src/ProtoFast.Segmentation.Storage/ArtifactKeys.cs):

```csharp
/// <summary>The original the browser uploaded. For a .md upload this IS Upload(...) — the
/// passthrough case is literally "the source is already the markdown".</summary>
public static string UploadSource(string ownerSubject, string uploadId, string extension) =>
    $"{UploadsPrefix}{ownerSubject}/{uploadId}{extension}";

public static string UploadConversion(string ownerSubject, string uploadId) =>
    $"{UploadsPrefix}{ownerSubject}/{uploadId}.conversion.json";

public static string RunSourceLayout(string runId) => RunPrefix(runId) + "00_source_layout.json";
public static string RunConversion(string runId)   => RunPrefix(runId) + "00_conversion.json";
```

The extension is **derived by `api` from the validated allowlist**, never taken from the request's
filename. A filename is attacker-controlled; `SourceFormats` maps a validated media type to exactly
one canonical extension, and that is what goes in the key and in the signed policy.

### 13.1 Data model

`uploads` (see [`Upload.cs`](../services/segmentation/src/ProtoFast.Segmentation.Data/Entities/Upload.cs))
gains three columns, in one EF migration:

| Column | Type | Meaning |
|---|---|---|
| `MediaType` | `text` | The validated media type the policy was signed for |
| `SourceExtension` | `text` | Canonical extension; with `MediaType`, reconstructs the source key |
| `RequiresConversion` | `boolean` | False for `.md`/`.markdown`/`.txt` |

`WithLayout` keeps its name but changes meaning — it is no longer "the user also uploaded a layout
file" but "this format produces one". Worth a comment on the property saying so, because the old
reading is the wrong one.

## 14. The gRPC surface

Two changes to [`segmentation.proto`](../services/api/src/ProtoFast.Api/Protos/segmentation.proto).
Full diff in [Appendix C](#appendix-c-proto-diff).

**`CreateUpload`** now takes a media type and returns POST form fields:

```protobuf
message CreateUploadRequest {
  string file_name  = 1;
  int64  size_bytes = 2;
  reserved 3;                 // was with_layout — the format decides now, not the caller
  string content_type = 4;    // validated against the allowlist
}

message CreateUploadReply {
  string upload_id = 1;
  reserved 2, 3;              // was markdown_put_url / layout_put_url
  map<string, string> fields = 4;   // post verbatim, file LAST
  int64  expires_unix_seconds = 5;
  string post_url = 6;
  int64  max_bytes = 7;             // echoed so the client's message matches the policy exactly
}
```

Field numbers are reserved rather than reused: the feature is unreleased, but a reused number is
the kind of thing that silently mis-decodes a stale client during a partial deploy.

**`ListSourceFormats`** is new, and exists so the browser cannot drift from the server:

```protobuf
rpc ListSourceFormats (ListSourceFormatsRequest) returns (ListSourceFormatsReply);

message SourceFormat {
  string extension = 1;       // ".pdf"
  string media_type = 2;      // "application/pdf"
  string label = 3;           // "PDF"
  bool   produces_layout = 4;
  bool   ocr_capable = 5;
}

message ListSourceFormatsReply {
  repeated SourceFormat formats = 1;
  int64 max_bytes = 2;
}
```

`CreateUpload`'s validation becomes:

```csharp
if (!SourceFormats.TryResolve(request.FileName, request.ContentType, out var format))
{
    throw new RpcException(new Status(
        StatusCode.InvalidArgument,
        $"ThePlot cannot read {Path.GetExtension(request.FileName)} files yet."));
}

if (request.SizeBytes <= 0 || request.SizeBytes > _options.MaxUploadBytes)
{
    throw new RpcException(new Status(
        StatusCode.InvalidArgument,
        $"The document must be between 1 byte and {_options.MaxUploadBytes / (1024 * 1024)} MB."));
}
```

`SubmitRun`'s precondition check moves from the Markdown key to the **source** key — otherwise
every non-Markdown submission fails with "the document has not finished uploading", because at that
moment the Markdown genuinely does not exist yet.

## 15. ThePlot client

[`upload.ts`](../clients/theplot/src/app/pages/upload/upload.ts) and
[`segmentation-api.ts`](../clients/theplot/src/app/segmentation/segmentation-api.ts):

- `accept` is built from `ListSourceFormats` (fetched once, after hydration, alongside the page's
  other data) instead of the hardcoded `".md,.markdown,.txt"`.
- The dropzone copy changes from "Drop a Markdown file here" to "Drop a document here" with a
  sub-line listing families: "PDF, Word, PowerPoint, Excel, HTML, EPUB, images and Markdown — up to
  10 MB".
- The **"Add layout metadata" control is removed.** Layout is produced by the converter now; asking
  a user for a `.layout.json` from "their converter" no longer makes sense, and leaving the control
  in place would let a user's file silently lose to the converter's.
- `upload()` switches from `PUT` to a multipart `POST`:

```ts
private async postToS3(reply: CreateUploadReply, file: File, onProgress?: (f: number) => void) {
  const form = new FormData();
  for (const [name, value] of Object.entries(reply.fields)) {
    form.append(name, value);
  }
  form.append('file', file);        // MUST be last: S3 stops reading at the file part

  // XHR rather than fetch, because upload progress is the point (fetch has no upload progress
  // event) and this is the slow part of the whole submission.
  …
}
```

- Client-side pre-check: `file.size > reply.maxBytes` short-circuits with the same message S3 would
  have produced, so the common case is instant and the wording is identical either way.
- The filename→name derivation drops its `\.(md|markdown|txt)$` regex for the allowlist's
  extensions.
- [`phases.ts`](../clients/theplot/src/app/segmentation/phases.ts): phase 0's blurb becomes
  "Converting the document and reading its layout". The index stays 0.

## 16. Changes to existing code

| File | Change |
|---|---|
| [`SegmentationApiOptions.cs`](../services/api/src/ProtoFast.Api/Segmentation/SegmentationApiOptions.cs) | `MaxUploadBytes` 64 MiB → **10 MiB**; the "cannot enforce a size" comment is replaced by one describing the policy condition |
| [`SegmentationService.cs`](../services/api/src/ProtoFast.Api/Segmentation/SegmentationService.cs) | `CreateUpload` → presigned POST + format validation; `SubmitRun` checks the source key; new `ListSourceFormats` |
| [`segmentation.proto`](../services/api/src/ProtoFast.Api/Protos/segmentation.proto) | Appendix C |
| [`IArtifactStore.cs`](../services/segmentation/src/ProtoFast.Segmentation.Storage/IArtifactStore.cs) | `IPresignedUrlFactory` gains `PresignPost`; new `PresignedPost` record |
| [`S3ArtifactStore.cs`](../services/segmentation/src/ProtoFast.Segmentation.Storage/S3ArtifactStore.cs) | Implements `PresignPost`; needs the resolved `AWSCredentials` injected (presigned POST signs with credentials directly, unlike `GetPreSignedURL`) |
| [`ArtifactKeys.cs`](../services/segmentation/src/ProtoFast.Segmentation.Storage/ArtifactKeys.cs) | `UploadSource`, `UploadConversion`, `RunSourceLayout`, `RunConversion` |
| [`StorageOptions.cs`](../services/segmentation/src/ProtoFast.Segmentation.Storage/StorageOptions.cs) | New `ConversionOptions` (`Endpoint`, `Timeout`, `OcrEnabled`, `OcrLanguages`, `MaxOcrPages`) |
| [`Upload.cs`](../services/segmentation/src/ProtoFast.Segmentation.Data/Entities/Upload.cs) + `Migrations/` | Three columns; one migration |
| [`DeterministicExecutors.cs`](../services/segmentation/src/ProtoFast.Segmentation.Pipeline/Executors/DeterministicExecutors.cs) | `IngestExecutor` converts before reading; copies layout and report under the run prefix |
| `SourceFormats.cs` (new, `Core/Ingest/`) | The allowlist |
| `DocumentConverter.cs` (new, `Pipeline/Ingest/`) | The HTTP client of §9.1 |
| [`upload.ts`](../clients/theplot/src/app/pages/upload/upload.ts), [`segmentation-api.ts`](../clients/theplot/src/app/segmentation/segmentation-api.ts), [`phases.ts`](../clients/theplot/src/app/segmentation/phases.ts) | §15 |
| [`apphost/Program.cs`](../apphost/Program.cs) | `AddDockerfile("conversion", …)`; `Seg_Conversion__Endpoint` on the worker |
| [`localstack-init.sh`](../scripts/localstack-init.sh) | CORS `AllowedMethods` gains `POST` |
| [`infra/segmentation.tf`](../infra/segmentation.tf) | `allowed_methods = ["PUT", "POST"]`; `allowed_headers` unchanged (`content-type` covers the form part) |
| [`infra/variables.tf`](../infra/variables.tf) | `ecr_repositories` gains `protofast-conversion` |
| [`deploy/docker-compose.host-services.yml`](../deploy/docker-compose.host-services.yml) | New `conversion` service |
| [`deploy/deploy.sh`](../deploy/deploy.sh) | `conversion` in the component list and the health check |
| `.github/workflows/deploy-conversion.yml` (new) | `build: buildx`, `host: services`, `target: protofast-conversion` |

## 17. Configuration contract

### 17.1 The worker's side (.NET, `Seg_` prefix)

| Variable | Dev (AppHost) | Prod (compose) | Default |
|---|---|---|---|
| `Seg_Conversion__Endpoint` | the Aspire endpoint of the `conversion` resource | `http://conversion:8090` | — |
| `Seg_Conversion__Timeout` | `00:10:00` | `00:10:00` | 10 min |
| `Seg_Conversion__OcrEnabled` | `true` | `true` | `true` |
| `Seg_Conversion__OcrLanguages__0` | `eng` | `eng` | `eng` |
| `Seg_Conversion__MaxOcrPages` | `200` | `200` | 200 |

### 17.2 `api`'s side

| Variable | Value |
|---|---|
| `Api_Segmentation__MaxUploadBytes` | `10485760` |

### 17.3 The converter's side

The converter is not a .NET process, so it does not follow the `Prefix_Section__Key` convention —
the same deviation the Envoy and OTel containers already make. Plain `CONVERSION_*` names:

| Variable | Dev | Prod |
|---|---|---|
| `CONVERSION_BUCKET` | `protofast-segmentation-dev` | `${SEGMENTATION_BUCKET}` |
| `CONVERSION_S3_ENDPOINT` | the LocalStack gateway | *(unset — the SDK resolves AWS)* |
| `AWS_REGION` / `AWS_DEFAULT_REGION` | `us-west-2` | `${AWS_REGION}` |
| `CONVERSION_MAX_SOURCE_BYTES` | `10485760` | `10485760` |
| `CONVERSION_MAX_OUTPUT_BYTES` | `26214400` | `26214400` |
| `CONVERSION_TIMEOUT_SECONDS` | `480` | `480` |
| `CONVERSION_OCR_LANGUAGES` | `eng` | `eng` |
| `CONVERSION_OCR_MIN_CONFIDENCE` | `60` | `60` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | the collector | `http://${HOST_A_IP}:4317` |

No secrets. The converter holds no credentials of its own — S3 access comes from the instance role
over IMDS in production and from LocalStack's throwaway credentials in dev.

## 18. AWS infrastructure

| Resource | Change |
|---|---|
| `aws_s3_bucket_cors_configuration.segmentation` | `allowed_methods = ["PUT", "POST"]`. POST is the new upload verb; PUT stays until the client is migrated, then can be dropped |
| `ecr_repositories` | `+ protofast-conversion` |
| IAM (`infra/segmentation.tf`) | No new statement needed: the converter's `GetObject`/`PutObject` are under `uploads/`, already inside the `s3:prefix` condition. Confirm the condition covers `uploads/*` for both verbs |
| Lifecycle | Unchanged. The source, the Markdown, the layout and the report all live under `uploads/` and expire together at 7 days |
| Security groups | None. The converter publishes no port; only the worker on the same host dials it |

No new queue, no new database, no new secret.

## 19. Local development

1. `AddDockerfile("conversion", "services/conversion")` in the AppHost, next to the existing
   Dockerfile-based resources (the Envoy proxy and the clients host already use this pattern). A
   container rather than `AddPythonApp` because the converter needs Tesseract, Ghostscript and
   ExifTool on the box — nobody should have to install those to run `aspire run`.
2. The worker `.WaitFor(conversion)` and gets `Seg_Conversion__Endpoint`.
3. [`localstack-init.sh`](../scripts/localstack-init.sh) adds `POST` to the CORS rule's
   `AllowedMethods`, alongside the existing `PUT`, `GET`, `HEAD`.
4. **[verify]** LocalStack enforces `content-length-range` on POST policies. If it does not, dev
   silently accepts an 11 MiB file that production would refuse — the worst kind of divergence.
   The integration test in §24 asserts the refusal and will fail loudly on a LocalStack that does
   not implement it; if that happens, note it in the test and rely on the converter's
   `ContentLength` backstop in dev.
5. First `aspire run` after this lands pulls a ~700 MB image (Tesseract's data files and
   Ghostscript dominate). Worth calling out in [layer 02](02-local-development.md).

## 20. Deployment

`.github/workflows/deploy-conversion.yml`, modelled on the existing per-component workflows:

```yaml
jobs:
  deploy:
    uses: ./.github/workflows/_component-deploy.yml
    with:
      component: conversion
      host: services
      build: buildx                 # a Dockerfile component, cross-built for arm64 like envoy
      target: protofast-conversion
      kind: service
      hash_paths: "services/conversion"
      project: services/conversion
      tag: ${{ inputs.tag }}
    secrets: inherit
```

Compose (`deploy/docker-compose.host-services.yml`):

```yaml
  conversion:
    image: ${ECR}/protofast-conversion:${CONVERSION_TAG}
    restart: unless-stopped
    # No published port: only the segmentation worker on this host dials it, by name.
    environment:
      CONVERSION_BUCKET: ${SEGMENTATION_BUCKET}
      AWS_REGION: ${AWS_REGION}
      AWS_DEFAULT_REGION: ${AWS_REGION}
      OTEL_EXPORTER_OTLP_ENDPOINT: "http://${HOST_A_IP}:4317"
    mem_limit: 1g
    read_only: true
    tmpfs:
      - /tmp:size=512m               # conversion is file-based; this is the only writable path
    cap_drop: [ALL]
    security_opt: [no-new-privileges:true]
    healthcheck:
      test: ["CMD", "python", "-c", "import urllib.request;urllib.request.urlopen('http://localhost:8090/health')"]
      interval: 10s
      timeout: 3s
      retries: 5
      start_period: 20s
```

**Ordering.** The converter must be deployed before the worker that calls it, and the worker must
tolerate its absence — an unreachable converter fails phase 0 transiently, so runs queue rather than
die. Deploying conversion first, then segmentation, makes that window empty.

**Rollback** is per-component as everywhere else: `deploy.sh apply conversion=<old-tag>`.

## 21. Security and data governance

| Concern | Control |
|---|---|
| **Upload size as a DoS vector** | The POST policy refuses at S3; nothing downstream ever sees the bytes (§7) |
| **Wrong bytes under an allowed extension** | The converter re-sniffs content before dispatch; MarkItDown's own detection is not trusted to be safe on a mislabelled file |
| **Malicious documents** (XXE, zip-slip via OOXML, PDF JS, font exploits) | Read-only rootfs, `cap_drop: ALL`, `no-new-privileges`, 1 GB memory cap, tmpfs-only writes, no egress but S3. XML parsing uses `defusedxml` where MarkItDown lets us supply the parser — **[verify]** whether it does; if not, note it as accepted residual risk with the sandbox as the mitigation |
| **Data exfiltration by a converter dependency** | Network egress restricted to the S3 endpoint; the dependency list is explicit and locked; no LLM or cloud-AI client package is installed at all (asserted in CI) |
| **Cross-tenant key access** | The POST policy pins `key` exactly, and the key embeds the caller's subject from the internal JWT — as `CreateUpload` already does |
| **Sensitivity** | Conversion is local, so `RESTRICTED` documents convert like any other; the provider-routing rules apply from phase 3 onward, unchanged |
| **Retention** | The original is under `uploads/` and expires in 7 days with the rest. The run keeps its own copy of the Markdown, not of the original — a run cannot be re-run from the source PDF after a week, only from its Markdown, which is an accepted trade and worth stating in the UI |
| **PII in logs** | The converter logs keys, sizes, page counts and durations. Never document text, never OCR output, never filenames beyond their extension |

## 22. Observability

Spans, on the existing trace (the `traceparent` in the request body is what joins them):

- `conversion.convert` — attributes `media_type`, `source_bytes`, `pages`, `ocr.applied`,
  `markdown_bytes`, `duration_ms`
- `conversion.ocr` — `pages_ocred`, `mean_confidence`, `engine`
- `conversion.layout` — `lines`, `produced`

Metrics: `conversion_duration_seconds` (histogram, by `media_type` and `ocr_applied`),
`conversion_failures_total` (by `code`), `conversion_ocr_pages_total`,
`upload_rejected_total` (by reason: `too_large`, `unsupported_format`).

`upload_rejected_total{reason="too_large"}` is the one to watch after launch: a high rate means
10 MiB is the wrong number, not that users are misbehaving.

Run events: phase 0 emits `converting <format>`, then `converted: N pages, OCR applied` (or not),
so the phase ladder in ThePlot explains a long phase 0 instead of just sitting on it.

## 23. Failure modes

| Failure | Detection | Response |
|---|---|---|
| File over 10 MiB | S3 `EntityTooLarge` | Client message; no object created; nothing to clean up |
| Unsupported format | `CreateUpload` rejects | No URL minted |
| Allowed extension, unreadable bytes | Converter 422 | Phase 0 fails permanently with the format and reason; no SQS retries |
| Converter unreachable / restarting | HTTP connect failure | Transient: one retry, then the message returns to the queue and the run resumes after the deploy |
| Conversion exceeds 8 min | Converter 504 | One retry; second failure fails the run with "this document takes too long to convert" |
| OCR produces near-empty text | `meanConfidence` below floor, or < 100 chars/page after OCR | Run continues with a warning event; the user sees why their result is thin |
| Converter OOM-killed | Container restart, no response | Same path as unreachable; the memory cap keeps it from taking the host with it |
| MarkItDown emits gigantic Markdown (a spreadsheet of a million rows) | Output over `CONVERSION_MAX_OUTPUT_BYTES` | 422 with "this document converts to more text than ThePlot can segment" |
| LocalStack does not enforce the policy | The §24 integration test | Dev-only divergence, documented; the converter's `ContentLength` check still refuses |

## 24. Testing

**Unit (Python, `services/conversion/tests`)** — one fixture per format family, asserting: Markdown
is non-empty; layout is emitted only where §6 says; OCR triggers on a scanned fixture and not on a
text-layer one; the guards refuse oversize, over-page and over-time inputs.

**Unit (.NET)** — `SourceFormats` resolution (extension/media-type agreement, unknown types,
case-insensitivity, no path traversal in the derived key); the presigned-POST policy JSON contains
the `content-length-range` condition with the configured maximum.

**Contract** — `formats.py` and `SourceFormats.cs` are asserted equal by a test that reads both, so
the two lists cannot drift.

**Integration (`ProtoFast.Segmentation.IntegrationTests`, against LocalStack)** —
(a) an 11 MiB POST is refused by S3 and leaves no object;
(b) a small PDF fixture converts, and phase 0 produces lines with layout;
(c) a `.md` upload never calls the converter (asserted with a stub that fails if called);
(d) re-running from phase 0 does not re-convert.

**CI guard** — a step that greps the converter image's `pip list` for `openai`, `anthropic`,
`azure-ai-*`, `torch` and fails if any appear, and a `test -d /usr/local/cuda` that must fail.
This is acceptance criterion 6 and it is the only thing that will keep §3 true in a year.

**Gold set** — add one converted-from-PDF document and one OCR'd document to `tests/gold-dev`, so
the segmentation thresholds are measured against real conversion output and not only against
hand-written Markdown.

## 25. Capacity

On a `t4g.medium` (2 vCPU, ARM), one converter process:

| Case | Wall clock |
|---|---|
| `.md` passthrough | ~0 (no call) |
| 2 MiB text-layer PDF, 40 pages | 3–8 s |
| 10 MiB text-layer PDF, 300 pages | 25–60 s |
| Scanned PDF, 40 pages, Tesseract | 60–150 s (1.5–3 s/page/core) |
| `.docx` / `.pptx` / `.xlsx` | < 2 s |
| Single image, OCR | 1–3 s |

The 200-page OCR cap exists because of the bottom-right of that table: a 10 MiB bilevel scan can
carry several hundred pages, and 300 pages of OCR would hold a worker slot for fifteen minutes.

Conversion is CPU-bound and the worker's eight concurrent runs are not; one converter replica with
a single worker process is the right starting point, with `MaxConcurrentRuns` unchanged. If phase-0
queueing shows up in the traces, the next step is two Uvicorn workers, then a second replica —
neither requires a design change, because the converter is stateless.

## 26. Milestones

| # | Deliverable | Done when |
|---|---|---|
| 1 | `SourceFormats` + presigned POST + 10 MiB cap, Markdown-only still | An 11 MiB `.md` is refused by S3; a small `.md` still runs end to end |
| 2 | Converter service: passthrough + PDF text layer + pdfplumber layout | A text-layer PDF reaches `Publish` with `HasLayout` true |
| 3 | The office and web formats (`docx`, `pptx`, `xlsx`, `html`, `epub`, data, `msg`, `ipynb`) | Acceptance criterion 4 |
| 4 | OCR: scanned PDFs and images | Acceptance criteria 3 and the image row of §6 |
| 5 | Client: server-driven accept list, POST upload, copy changes | Acceptance criterion 7 |
| 6 | Infra, compose, workflow, CI guard, gold documents | Acceptance criteria 6 and 8; the CI guard is green and *has been seen to fail* on a deliberately added `openai` dependency |

Milestone 1 ships the size limit on its own, before any converter exists. That ordering is
deliberate: the limit is the piece with a security argument behind it, and it should not wait on
the piece with a Python image behind it.

## 27. Open questions

1. **Is 10 MiB the right number?** It refuses a fair number of real scanned books. The limit is one
   config value and one policy condition, so raising it is cheap — but it is also the only thing
   bounding conversion cost. Recommend shipping at 10 MiB and watching
   `upload_rejected_total{reason="too_large"}` for a month.
2. **`.zip`** — MarkItDown iterates archives, and users will try it. Needs an expansion-ratio cap, a
   per-entry size cap, an entry-count cap and zip-slip protection before it can be allowed. Deferred.
3. **Attachments in `.msg`** — same recursion question as `.zip`, same answer for now.
4. **Multi-language OCR** — the image ships `eng` only. Adding language packs is a Dockerfile line
   and an env var, but language *detection* is a real decision (per-document setting in the upload
   form vs. automatic). Recommend a form field when the first non-English document appears.
5. **Should the user see the converted Markdown before segmentation runs?** It would catch bad
   conversions before spending model budget, but it adds a second human gate to a pipeline that
   already has one. Recommend: no gate, but expose the converted Markdown as a downloadable
   artifact on the run page so a bad result is diagnosable.
6. **Keeping the original longer than 7 days** — re-running from the source rather than from the
   Markdown requires it. Needs a decision on cost and on what "delete my document" then means.
7. **PyMuPDF** — better layout, AGPL. Only worth reopening with a commercial licence.

---

## Appendix A: format matrix

| Extension | Media type | MarkItDown converter | Extra | Layout source | OCR |
|---|---|---|---|---|---|
| `.md` `.markdown` | `text/markdown` | *(passthrough)* | — | — | — |
| `.txt` | `text/plain` | *(passthrough)* | — | — | — |
| `.pdf` | `application/pdf` | PDF (`pdfminer.six`) | `[pdf]` | pdfplumber | conditional |
| `.docx` | `application/vnd.openxmlformats-officedocument.wordprocessingml.document` | DOCX (`mammoth`) | `[docx]` | — | — |
| `.pptx` | `application/vnd.openxmlformats-officedocument.presentationml.presentation` | PPTX (`python-pptx`) | `[pptx]` | — | — |
| `.xlsx` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` | XLSX (`openpyxl`) | `[xlsx]` | — | — |
| `.xls` | `application/vnd.ms-excel` | XLS (`xlrd`) | `[xls]` | — | — |
| `.html` `.htm` | `text/html` | HTML | — | — | — |
| `.csv` | `text/csv` | CSV | — | — | — |
| `.tsv` | `text/tab-separated-values` | CSV | — | — | — |
| `.json` | `application/json` | JSON | — | — | — |
| `.xml` | `application/xml` | XML | — | — | — |
| `.epub` | `application/epub+zip` | EPUB | — | — | — |
| `.msg` | `application/vnd.ms-outlook` | Outlook | `[outlook]` | — | — |
| `.ipynb` | `application/x-ipynb+json` | Notebook | — | — | — |
| `.png` `.jpg` `.jpeg` `.tif` `.tiff` `.bmp` | `image/*` | *(ours: Tesseract)* | — | `image_to_data` | always |

**[verify]** every converter name and extra against the pinned MarkItDown; the extras list in
particular has been reorganised between releases.

## Appendix B: `conversion.json`

Written to `uploads/{sub}/{uploadId}.conversion.json` and copied to `runs/{runId}/00_conversion.json`.

```json
{
  "uploadId": "upl_01J8…",
  "sourceKey": "uploads/9f2…/upl_01J8….pdf",
  "sourceBytes": 3145728,
  "sourceHash": "sha256:…",
  "mediaType": "application/pdf",
  "converter": { "name": "markitdown", "version": "0.1.x", "extractor": "pdfplumber" },
  "markdown": { "key": "uploads/9f2…/upl_01J8….md", "bytes": 184213, "hash": "sha256:…" },
  "layout":   { "key": "uploads/9f2…/upl_01J8….layout.json", "lines": 4821 },
  "pages": 42,
  "ocr": { "applied": true, "engine": "tesseract", "version": "5.3.0",
           "languages": ["eng"], "pagesOcred": 42, "meanConfidence": 87.4 },
  "warnings": ["3 pages produced no text after OCR"],
  "startedAt": "2026-09-17T18:22:04Z",
  "durationMs": 41230
}
```

`sourceHash` is what makes conversion idempotent: it is stamped on the Markdown object as
`x-amz-meta-pf-source-hash`, and a second conversion of the same source short-circuits on it.

## Appendix C: proto diff

```diff
 service Segmentation {
-  // Returns a presigned S3 PUT URL. The browser uploads directly to S3, so a large document
-  // never crosses gRPC and api never sees the bytes.
+  // Returns a presigned S3 POST (url + form fields). The browser uploads directly to S3, so a
+  // large document never crosses gRPC and api never sees the bytes. POST rather than PUT because
+  // only a POST policy can carry a content-length-range condition — which is what makes the size
+  // limit S3's to enforce rather than the browser's to respect.
   rpc CreateUpload (CreateUploadRequest) returns (CreateUploadReply);
+
+  // The formats ThePlot can read and the size cap, so the upload page's accept list and its error
+  // messages come from the same place the validation does.
+  rpc ListSourceFormats (ListSourceFormatsRequest) returns (ListSourceFormatsReply);

 message CreateUploadRequest {
   string file_name = 1;
   int64 size_bytes = 2;
-  bool with_layout = 3;               // also presign the .layout.json sibling
+  reserved 3;                         // was with_layout; the format decides now, not the caller
+  string content_type = 4;            // validated against the allowlist
 }

 message CreateUploadReply {
   string upload_id = 1;
-  string markdown_put_url = 2;
-  string layout_put_url = 3;          // empty when with_layout is false
-  map<string, string> required_headers = 4;
+  reserved 2, 3;                      // were markdown_put_url / layout_put_url
+  map<string, string> fields = 4;     // post verbatim as multipart fields, with the file LAST
   int64 expires_unix_seconds = 5;
+  string post_url = 6;
+  int64 max_bytes = 7;                // the same number the policy enforces
 }
+
+message ListSourceFormatsRequest {}
+
+message SourceFormat {
+  string extension = 1;
+  string media_type = 2;
+  string label = 3;
+  bool produces_layout = 4;
+  bool ocr_capable = 5;
+}
+
+message ListSourceFormatsReply {
+  repeated SourceFormat formats = 1;
+  int64 max_bytes = 2;
+}
```
