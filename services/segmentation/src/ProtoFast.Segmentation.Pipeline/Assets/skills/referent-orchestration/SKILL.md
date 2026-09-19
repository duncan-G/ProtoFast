# Building the persona registry

Each window agent has reported the referents it is confident about **inside its own window**. You
see those reports — surface forms, how often each occurs, and what each window could not settle.
You decide which of them are the same person, place or thing across the whole document.

## What you return

One row per registry entry. Each row names the candidate references that make it up, the name the
entry should carry, and what kind of entry it is.

You name **candidates**, never surface forms of your own and never paragraph ids. You compose
references code issued; you do not write content.

## The rules

- **Every candidate is assigned exactly once.** A candidate that merges with nothing still gets a
  row of its own. A candidate left out means the plan cannot be applied.
- **`canonicalName` should be one of the candidate's own surface forms** — the most formal one the
  text uses. It is generated metadata and never re-enters the document, but a name the text never
  used is a name no reader will recognise.
- **Merge only what you are confident about.** "Dr. Vance" in window 1 and "the professor" in
  window 3 merge when the windows' reports support it. Two characters who share a first name do
  not.
- **A merge you get wrong is worse than a merge you miss.** A wrong merge blends two people's
  voices and appearances for the rest of the document, and it is nearly invisible in review. A
  missed merge is one duplicate entry somebody can see and fix.
- **Some documents hold several works.** Where they do, candidates from different works are never
  the same person, and a plan that merges across them cannot be applied. An "Alice" in one story
  and an "Alice" in another are two Alices.

## Scope

`persistent` is the default: someone who appears across the document. Use `scene-local` for the
throwaway names of worked examples and exercises — a textbook's two hundred Alices, each alive for
one problem.

## Groups

Set `kind: "group"` for a collective. `formation` is `enumerated` when the text spells out the
members and `named` when it does not.

## Questions

If an answer from one window would change what you write, ask — but only with the question kinds
you are given, and only about candidates a window reported. If nothing would change, return the
registry.
