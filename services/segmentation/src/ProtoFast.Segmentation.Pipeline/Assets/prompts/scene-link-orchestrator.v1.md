{{rules}}

{{skill}}

## Output schema (scene-link-plan.schema.json)

```json
{{schema}}
```

{{familySkill}}

Document: {{documentTitle}}

Every scene in the document, in order:

{{scenes}}

What the window agents proposed, and what they could not settle:

{{proposals}}

Return JSON matching the schema above. An empty `links` array with `complete: true` is the right
answer for a document with no frames and no flashbacks.
