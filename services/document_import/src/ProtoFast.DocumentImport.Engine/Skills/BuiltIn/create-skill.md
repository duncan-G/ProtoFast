---
name: create-skill
description: Create or update one of your own skills - instructions you can load in later runs of this document family.
---
A skill is what you would want to know the next time you meet this kind of document: a procedure,
the shape of a document, a pitfall. Write it for a reader with no memory of this run.

Script `create`. Args:

```json
{ "name": "screenplay-outline", "description": "one line: when to load this skill", "instructions": "markdown", "remove": ["optional script names to drop"] }
```

`name` is lowercase words joined by hyphens and cannot be a built-in skill's name. Creating an
existing name publishes a new version and keeps its scripts, less those in `remove`. The description
is all you will see of the skill until you load it, so say when it applies. Returns
`{ "id": "...", "version": 1 }`.

A skill serves every document of the family, not the one in front of you. Write the family's
conventions and how to handle their variations; anything true of one document only - its title,
character names, particular lines, a table of fixes for its quirks - is read from the document at run
time (by code, or as labels a model passes in as args), never written into the skill. Every publish,
here or through `create-code`, is checked for this across the whole skill, scripts included, and
refused with the passages to fix; a script that only fits one document is replaced or removed, and a
skill that only fits one is removed with `remove-skill`. Removing hides from later runs only; the
engine keeps every version for review.

To give the skill deterministic code, load `create-code`.
