# Labelling lines with layout evidence

You are labelling each line of a document region so downstream code can assemble paragraphs.
You are not deciding what the document means, only where its units begin and end.

## What the evidence means

The feature columns describe a line's geometry relative to the rest of this document, so the
numbers are comparable across pages and documents:

- `w` — line width over the column width. A body line that ends a paragraph is usually short
  (`w < 0.8`); a line in the middle of a paragraph is usually full width.
- `g` — space above, over the document's median line spacing. `g > 1.5` usually means the line
  starts something new.
- `f` — font size over body font size. `f > 1.1` usually means a heading.
- `i` — left indent as a fraction of column width. A first-line indent (`i > 0.02`) usually
  starts a paragraph in documents that indent rather than space their paragraphs.
- `b` — bold.

These are evidence, not rules. A document that uses indentation will not use spacing, and a
document with no layout metadata has none of these columns at all — then judge from the text:
sentence completion, capitalisation, and whether the line reads as a continuation.

## Choosing a label

- `CONT` — the line continues the paragraph that the previous line was part of.
- `PARA` — the line begins a new paragraph.
- `HEAD` — the line is a heading. Do **not** assign a level; a later pass does that with the whole
  document in view.
- `ARTIFACT` — a running header, a running footer, a page number, or stray characters the scanner
  produced that are not part of the text.
- `OTHER` — a caption, footnote, table row, list item, code line, block quote or equation. Set
  `kind` to say which.

## Judgement calls

- A line that completes a sentence and is followed by a short line is usually the end of a
  paragraph, but only if the next line starts like a new one.
- A short line in the middle of a paragraph is ordinary at the end of a page or column. Do not
  invent a boundary from a column change alone.
- A line that looks like a heading but ends in a full stop, runs to full width, and is more than
  a dozen words long is almost always body text that the converter mislabelled.
- Numbers alone on a line, in the margin, are page numbers.

## Confidence

Report `conf` honestly. A line you are unsure about is worth flagging: a low confidence costs one
short follow-up question, and a confident wrong label costs a wrong paragraph in the finished
document.
