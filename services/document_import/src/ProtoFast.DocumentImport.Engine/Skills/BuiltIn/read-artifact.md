---
name: read-artifact
description: Read an artifact's text, a page at a time.
---
Script `read-artifact`. Args:

```json
{ "artifact": { "runId": "...", "stageId": "...", "hash": "..." }, "offset": 0, "length": 50000 }
```

`offset` and `length` are characters and optional. Returns
`{ "text": "...", "offset": 0, "length": 50000, "totalLength": 182000 }`; read on from `offset + length`
while it is less than `totalLength`.

A script reads artifacts with `context.ReadTextAsync(artifact)` instead.
