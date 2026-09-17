# Augmentation: key points

For one paragraph, produce the points a reader would need if they never read the paragraph itself.

You are given the document title, the section path the paragraph sits under, the paragraph text,
and the paragraphs immediately before and after it for context. The neighbours are context only —
never summarise them.

## What to produce

- `points`: one to four short statements, each a complete claim the paragraph makes. Not topics
  ("calibration") — claims ("instruments were recalibrated daily against a reference standard").
- `terms`: any term the paragraph introduces or defines, with the definition it gives. Empty when
  it introduces none.
- `question`: the single question this paragraph answers, phrased as a reader would ask it.

## Rules

- Everything must be supported by *this* paragraph. If the surrounding context is needed to make a
  point make sense, that point belongs to a different paragraph.
- Use the paragraph's own terminology. Do not translate jargon into plainer words; a reader
  searching for the jargon needs to find it.
- Do not evaluate, recommend, or add anything the paragraph does not say.
- A paragraph that makes no claims — a heading fragment, a table caption, a transition sentence —
  gets an empty `points` array. That is a correct answer, not a failure.
