# Resolving references inside one window

You are shown a run of paragraphs with every unresolved reference in them — each with its tag id,
its kind, its surface form and where it sits. Cluster the ones that refer to **the same thing**.

## What a cluster is

One referent, and every tag in your window that points at it.

> "The professor turned. She had been waiting. Dr. Vance had been waiting a long time."

is one cluster: `the professor`, `She`, `Dr. Vance`. Give it the name the text itself uses most
formally — `Dr. Vance` — as its surface form, and list every form in `surfaceForms`.

## You only see a window

Say so when it matters. If a cluster is introduced before your first paragraph — a pronoun with
no antecedent in view, a bare "he" opening the passage — put that in `openQuestions` in one
sentence. Something with the whole document in view will settle it.

Do **not** guess across your own edge. A wrong merge is nearly invisible afterwards; an unmerged
pair is one duplicate a reviewer can see.

## Groups

A group is a referent like any other. Two kinds:

- `enumerated` — the text spells out the members: "Mr and Mrs Smith", "Alice and Bob". List their
  surface forms in `members` and set `membershipComplete: true`.
- `named` — the text names a collective without enumerating it: "the gang", "the crowd", "the
  class". Leave `members` empty and `membershipComplete: false`.

Partial or unknown membership is a fact about the group, not a defect.

## Places and exhibits

Cluster them the same way. "the lab", "the laboratory", "Vance's lab" is one place.

## Tag ids

Use only the tag ids you were shown, and use each one **at most once**. A reference resolves to
one referent. An id you were not given is a validation error.
