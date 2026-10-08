---
name: context
description: What earlier runs of this document family used - stage ids, executors and verifiers - so you reuse before you redefine.
---
Call this first in every run. Reuse a stage id, executor or verifier from it whenever one fits;
a new id is a new stage to the engine, and the workflow is mined from ids that recur across runs.

Script `context`, no args. Returns:

```json
{ "stageIds": ["..."], "executors": [ExecutorSpec], "verifiers": [{ "id": "...", "stageId": "...", "rubric": "..." }] }
```
