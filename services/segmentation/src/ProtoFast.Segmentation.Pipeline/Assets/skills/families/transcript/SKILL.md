# Family: transcript

A recorded conversation, so the structure is turns rather than paragraphs.

- **A speaker label starts a turn.** `ALICE:`, `Q.`, `THE COURT:` — each begins a new paragraph,
  and the label is part of the text.
- **A long turn may contain several paragraphs**, but only break it where the speaker plainly
  changes subject. When in doubt, one turn is one paragraph.
- **Timestamps** (`[00:14:32]`) are `ARTIFACT` when they sit alone, and part of the text when they
  are inline.
- **Interjections and crosstalk** (`[inaudible]`, `[laughter]`) stay with the turn they interrupt.
- **There are usually no headings.** Do not manufacture them from topic changes at labelling time;
  section inference is a later pass with the whole conversation in view.
