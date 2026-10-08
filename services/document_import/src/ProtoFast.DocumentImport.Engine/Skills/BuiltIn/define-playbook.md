---
name: define-playbook
description: Publish the instructions a model executor runs on.
---
A playbook is the system prompt of a delegate. The stage's input artifacts become its user turn,
each wrapped in a tag named after the stage that produced it (the run input is `<manuscript>`), and
it must reply with the output document and nothing else.

Script `define-playbook`. Args:

```json
{
  "id": "outline-writer",
  "instructions": "You are ... Reply with one JSON object ...",
  "examples": [{ "input": ArtifactRef, "output": ArtifactRef }],
  "rules": { "decision-key": "a rule learned from earlier runs" }
}
```

When a stage restructures its input's text, have the playbook reply with labels that point into the
input (unit numbers, offsets, short anchors) under a contract of its own, never with the text, and
let a script build the document from those labels and the input. A delegate that retypes a passage
drifts from it and pays for every word.

`examples` and `rules` are optional; examples must be existing artifacts. Publishing an existing id
makes a new version. Returns `{ "id": "...", "version": 1 }`.
