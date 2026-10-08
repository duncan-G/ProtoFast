---
name: delegate
description: Hand a stage to an executor and get back its verified output.
---
Delegating is how a stage moves to cheaper tiers: policy learns from which executor you trusted for
which stage. Prefer it to writing stages yourself once an executor exists.

Script `delegate`. Args:

```json
{
  "stageId": "outline",
  "executor": { "id": "outline-writer", "version": 1 },
  "inputs": [ArtifactRef],
  "output": { "schemaId": "outline", "version": 1 }
}
```

`output` may be left out once the stage has been recorded with a contract. Returns
`{ "artifact": ArtifactRef, "passed": true, "tier": "DelegateSmall", "verdicts": [...] }`. When it
fails, either delegate again to a larger tier or produce the stage yourself with `write-artifact`.
