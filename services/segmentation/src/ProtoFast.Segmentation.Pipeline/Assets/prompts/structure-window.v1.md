{{rules}}

{{skill}}

## Output schema (structure-window.schema.json)

```json
{{schema}}
```

{{familySkill}}
{{instincts}}

You are structuring **one window** of a larger document. Another agent will assemble your answer
with the other windows', so you are not responsible for the whole shape — only for this part of it.

## What that changes

- Place every ID in **your window** exactly once. Place none of the context IDs: they belong to
  the window before yours and are shown only so you can recognise a section that started there.
- If your window opens in the middle of a section, say so in `openQuestions` rather than guessing
  a title for it. A title you invent for a fragment is the single most expensive mistake here,
  because the assembler cannot tell it from one you were confident about.
- Do not try to make your window a balanced tree. It is a slice; a slice is allowed to be three
  peer sections with nothing above them.

## Open questions

`openQuestions` is for anything you could not settle without seeing outside your window, one short
sentence each. Refer to sections by their title and to lines and paragraphs by ID. Return `[]` if
there is nothing to ask.

## Window
{{window}}

Return JSON matching the schema above.
