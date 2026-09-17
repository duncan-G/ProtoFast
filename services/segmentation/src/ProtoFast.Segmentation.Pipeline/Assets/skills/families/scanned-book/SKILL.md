# Family: scanned book

This document was scanned and OCR'd. Expect the conversion to be imperfect in specific, repeatable
ways.

- **Running heads repeat.** The book title on verso pages and the chapter title on recto pages,
  often with a page number. They are `ARTIFACT` even when they read like headings.
- **Hyphenation is real.** Print justifies text, so words genuinely break across lines. A line
  ending in a hyphen followed by a lowercase continuation is almost always one word.
- **Chapter openings are visually distinct** — a large number or title, sometimes a drop capital
  whose first letter the OCR emitted as a separate line. A single stray capital on its own line at
  the start of a chapter is part of the following paragraph, not a heading.
- **Footnotes sit at the bottom of the page** in smaller type, often numbered. Label them
  `OTHER` with `kind: footnote`; they are not body paragraphs and they interrupt the text that
  surrounds them.
- **Small caps and italics carry meaning**, but bold at body size is rarely a heading in a book —
  it is emphasis.
