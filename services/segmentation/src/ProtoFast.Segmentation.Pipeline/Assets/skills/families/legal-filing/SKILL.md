# Family: legal filing

Court filings have a rigid structure that the converter usually preserves but rarely marks up.

- **Numbered paragraphs are the unit.** A filing numbers its paragraphs (`1.`, `2.`, …) and each
  number starts a paragraph — the number is part of the text, not a list marker to strip.
- **The caption block** at the top (court, parties, case number) is structure, not prose. Treat
  each line as its own short paragraph rather than running them together.
- **Line numbers in the left margin** are `ARTIFACT`. They are sequential, reset per page, and
  have nothing to do with the text.
- **Section headings are often all-caps and centred**, with no font size change. In this family,
  an all-caps line shorter than about twelve words, with space above it, is a heading even when
  `f` is 1.0.
- **Signature blocks and certificates of service** at the end are their own sections, not a
  continuation of the argument.
