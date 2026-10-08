---
name: define-executor
description: Define a delegate - a model tier plus a playbook - that you can hand a stage to.
---
Script `define-executor`. Args:

```json
{ "id": "outline-writer", "tier": "DelegateSmall", "playbook": { "id": "outline-writer", "version": 1 } }
```

Tiers: `DelegateLarge`, `DelegateMedium` and `DelegateSmall` run the playbook on that size of model.
Choose the smallest tier that passes the stage's verifiers; policy promotes and demotes from there.
Returns `{ "id": "...", "version": 1 }`. The executor belongs to this document family.
