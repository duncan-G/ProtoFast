---
name: create-skill
description: Create or update one of your own skills - instructions you can load in later runs of this document family.
---
A skill is what you would want to know the next time you meet this kind of document: a procedure,
the shape of a document, a pitfall. Write it for a reader with no memory of this run.

Script `create`. Args:

```json
{ "name": "screenplay-outline", "description": "one line: when to load this skill", "instructions": "markdown" }
```

`name` is lowercase words joined by hyphens and cannot be a built-in skill's name. Creating an
existing name publishes a new version and keeps its scripts. The description is all you will see of
the skill until you load it, so say when it applies. Returns `{ "id": "...", "version": 1 }`.

To give the skill deterministic code, load `create-code`.
