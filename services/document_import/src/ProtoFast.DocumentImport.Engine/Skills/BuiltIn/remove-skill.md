---
name: remove-skill
description: Remove one of your own skills from this document family, so no later run lists or loads it.
---
Script `remove`. Args:

```json
{ "name": "screenplay-outline", "reason": "one line: why the skill no longer serves the family" }
```

Remove a skill that only fits the documents it was written on, or that another skill has replaced,
rather than keep publishing around it. Its scripts go with it; to drop only some, use `create-skill`
with `remove`. The engine keeps its history for review, but you will not see it again, and
`create-skill` with the same name starts a new skill. Returns `{ "removed": "..." }`.
