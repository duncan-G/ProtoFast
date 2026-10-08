---
name: write-artifact
description: Record a stage output you produced yourself; the stage's verifiers judge it.
---
Every artifact you produce goes through here or through `delegate`, or the engine never sees it.

Script `write-artifact`. Args:

```json
{
  "stageId": "outline",
  "contract": { "schemaId": "outline", "version": 1 },
  "content": "the document, as a string or as JSON",
  "inputs": [{ "runId": "...", "stageId": "...", "hash": "..." }]
}
```

`inputs` are the artifacts the output was made from; they become the stage's dependencies, so name
exactly those. Returns `{ "artifact": ArtifactRef, "passed": true, "verdicts": [...] }`. A failed
verdict carries findings: fix them and write the stage again.
