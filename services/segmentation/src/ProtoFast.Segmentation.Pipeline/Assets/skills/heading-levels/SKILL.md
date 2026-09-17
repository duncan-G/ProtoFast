# Assigning heading levels

You are given every heading in one document, in reading order, with whatever evidence exists for
each. Assign a level from 1 to 6 so that the outline is coherent as a whole. This is a
document-wide judgement, which is why it is a separate pass from labelling.

## Evidence, in order of reliability

1. **Numbering.** `2` is level 1, `2.1` is level 2, `2.1.3` is level 3. If a document numbers its
   headings, the numbering is the answer and nothing overrides it.
2. **Font scale.** Within one document, distinct sizes form tiers: the largest is level 1, the
   next level 2. Treat sizes within 5% of each other as the same tier.
3. **Position and phrasing.** A heading that reads as a division of the one above it ("Data
   collection" after "Methods") is one level deeper.

## Constraints

- Levels may not skip on the way down: a level 1 may be followed by a level 2, never a level 3.
  They may skip on the way back up.
- Headings that are visually identical must get the same level.
- If the document has only one tier of headings, give them all level 1. An invented hierarchy is
  worse than a flat one.
