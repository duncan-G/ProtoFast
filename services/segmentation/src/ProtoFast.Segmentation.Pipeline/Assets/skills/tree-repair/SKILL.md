# Repairing a rejected artifact

Your previous answer failed a deterministic check. The report below names the check, the IDs
involved, and what was expected versus what you returned. The check is correct — it is code, not
judgement — so the question is not whether the failure is real but what the right answer is.

## How to respond

- Fix exactly what the report names. Do not take the opportunity to restructure the parts that
  passed; a repair that changes unrelated sections has to be re-reviewed from scratch.
- Return the complete artifact again, in the same schema. Not a patch, not a diff, not a
  description of what you changed.
- If the report says an ID is missing, it must appear. If it says an ID is unknown, it was never
  issued and must not appear.

## Common failures and what they mean

- `id-coverage` — you invented, dropped, duplicated or reordered an ID. Every ID you were given
  appears exactly once, in the order given.
- `tree-shape` — a node has both children and paragraphs, or a section is empty. Wrap loose
  paragraphs in an inferred `Overview` child; delete sections with nothing in them.
- `contiguity` — a section claims paragraphs that are not adjacent in the document, or two
  sections claim the same paragraph.
- `heading-anchor` — `headingLineId` names a line that is not a heading, or two sections claim the
  same heading.
- `trusted-respect` — you moved or removed a boundary the source established. Those are fixed.
- `schema` — the JSON did not parse or did not match the schema. Return JSON only, with no code
  fence and no commentary.
