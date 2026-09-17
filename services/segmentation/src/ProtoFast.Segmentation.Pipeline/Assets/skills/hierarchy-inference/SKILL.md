# Inferring document structure

You are given a skeleton: the document's headings and one short excerpt per paragraph, in reading
order. Build the section tree.

## What you are deciding

Where sections begin and end, how they nest, and — where the document has no heading — whether a
run of paragraphs is a section worth naming.

## Rules

- Every node has `children` or `paragraphs`, never both. Where a section has its own opening
  paragraphs *and* subsections, wrap those paragraphs in a child section titled `Overview` with
  `"inferred": true`.
- Use every paragraph exactly once, in document order.
- Correct a heading's level when the outline is incoherent, but do not invent headings.
- Infer a section only where the topic clearly shifts and the document has no heading there.
  Set `"inferred": true` and give it a concise, specific title — `Calibration procedure`, not
  `Section 3` and not `Details`.
- Do not create a single-paragraph section unless that paragraph is genuinely standalone.
- Return IDs. Never return paragraph text.

## Paragraph edits

If a section boundary clearly falls *inside* a paragraph, say so in `paragraphEdits` rather than
splitting anything yourself: give the paragraph ID and the 1-based sentence index the section
should start at. Use this sparingly — it is for a paragraph that visibly contains two topics, not
for one that is merely long.

## When you are unsure

A flatter tree that is right beats a deeper one that is guessed. If a run of paragraphs has no
clear internal division, leave it as one section.
