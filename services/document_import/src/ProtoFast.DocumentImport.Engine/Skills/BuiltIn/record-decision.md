---
name: record-decision
description: Name a choice you made and why, so it can become a playbook rule.
---
Script `record-decision`. Args:

```json
{ "key": "scene-split", "choice": "split on blank-line runs", "rationale": "the source has no headings", "confidence": 0.8 }
```

Keys are stable names for a kind of choice; reuse them across runs. Returns `{}`.
