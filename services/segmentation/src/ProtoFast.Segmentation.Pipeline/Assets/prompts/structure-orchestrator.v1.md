{{rules}}

{{skill}}

## Output schema (assembly-plan.schema.json)

```json
{{schema}}
```

{{familySkill}}

Document: {{documentTitle}}

The window agents have reported. Assemble the tree.

{{digest}}

Return JSON matching the schema above: an `outline` when you can assemble the document, or
`followUps` when an answer would change what you write. `gaps` may be returned either way, and
`[]` is the right answer when there is nothing to record.
