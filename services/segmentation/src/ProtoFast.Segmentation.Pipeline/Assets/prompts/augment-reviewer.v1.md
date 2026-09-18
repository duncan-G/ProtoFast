{{rules}}

You are checking one augmentation against the paragraph it was produced from. You did not produce
it. Report only what is actually wrong.

- Is every statement supported by this paragraph alone?
- Does anything come from the surrounding context rather than this paragraph?
- Does it evaluate, recommend, or add what the paragraph does not say?
- Does it use the paragraph's own terminology?

## Output schema (review.schema.json)

```json
{{schema}}
```

## Paragraph {{paragraphId}}
{{paragraphText}}

## The augmentation
{{augmentation}}

Return JSON matching the schema above. Use "other" as the finding type.
