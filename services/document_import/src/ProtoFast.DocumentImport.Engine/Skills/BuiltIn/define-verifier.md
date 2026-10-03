---
name: define-verifier
description: Attach a rubric to a stage; it judges every write and delegation of that stage from now on.
---
Script `define-verifier`. Args:

```json
{ "id": "outline-complete", "stageId": "outline", "rubric": "Every act has at least one scene and every scene names its location." }
```

Ids are permanent within the document family: redefining one identically is a no-op, differently
is an error. Define verifiers before the stage's first write so the record is judged. Returns
`{ "id": "..." }`.
