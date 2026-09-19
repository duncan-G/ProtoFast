# Separating the work from its apparatus

You are shown paragraphs that code has flagged as *possibly* not part of the displayable work,
with the reason each was flagged. For each one, say whether it is displayable text or metadata,
and if metadata, which class.

## The classes

| Class | What it is |
|---|---|
| `FrontMatter` | Title page, copyright, ISBN block, dedication, epigraph, table of contents, list of figures |
| `BackMatter` | Index, colophon, about-the-author, ad pages |
| `RunningApparatus` | Running headers and footers, page numbers, running titles, "continued on p. 4" |
| `NavigationalLabel` | A figure number, "Chapter 4" as a bare label, section numbering separated from its title |
| `ProductionNoise` | Watermarks, scanner artifacts, OCR junk, crop marks |

## The one rule that matters

**When you are not sure, answer `displayable`.**

This is not politeness about uncertainty. Metadata is *withheld from the scene stream and never
deleted*, so a paragraph you wrongly leave in is visible to a reviewer and costs one line of
noise. A paragraph you wrongly take out is the author's work, silently missing, and nobody will
be looking for it. Under-removal is recoverable; over-removal is not.

## What counts as metadata is not universal

It depends on the kind of work, and the document's own family guidance is shown to you when it
exists:

- A dedication is apparatus in a textbook and arguably the work in a poetry collection.
- An epigraph opens a novel's chapter as content and heads a manual's section as apparatus.
- A bibliography is back matter in a novel and the point of a survey paper.

Where the guidance says nothing, prefer `displayable`.

## Confidence

Put a real number in `confidence`. A verdict below the run's floor is read as `displayable`
whatever it said, so an honest 0.4 on a doubtful copyright page does exactly what it should.

## Evidence

Cite the paragraph ids that justify your answer, and only ids you were shown. Never invent one.
