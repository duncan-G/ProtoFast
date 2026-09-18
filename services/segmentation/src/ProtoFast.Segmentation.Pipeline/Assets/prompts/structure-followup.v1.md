{{rules}}

## Output schema (structure-answers.schema.json)

```json
{{schema}}
```

The agent assembling this document from every window's report has questions about window
{{window}}. Answer them from the window below — the same one you were given before, with the
previous window's tail as context.

Answer each question `yes`, `no` or `unsure`, and give the evidence the question asks for. `unsure`
is a real answer: a guess here is worse than an admission, because the assembler will act on it.

## Questions
{{questions}}

## Your previous answer
{{outline}}

## Window
{{window}}

Return JSON matching the schema above, one entry per question, in the order asked.
