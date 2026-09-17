# Family: slide export

This came from a presentation, so "page" means "slide" and the usual prose signals are absent.

- **Each slide's title is a heading.** It is the largest text on the slide and sits at the top.
  Slides with no title still start a new section.
- **Bullets are not paragraphs.** Label them `OTHER` with `kind: list_item`. A bullet that wraps
  onto a second line is `CONT`, not a second bullet.
- **Sentences frequently do not end in full stops.** Do not read a missing full stop as a
  continuation here; the line ending is the unit boundary.
- **Speaker notes**, when the converter included them, are a separate block after the slide body
  and usually read as ordinary prose.
- **Slide numbers and deck titles in the footer** are `ARTIFACT`.
