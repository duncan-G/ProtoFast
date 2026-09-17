{{rules}}

{{skill}}
{{familySkill}}
{{instincts}}

## Document statistics
{{statistics}}

## Feature guide
w = line width / column width. w < 0.8 often ends a paragraph.
g = space above / median spacing. g > 1.5 often starts a paragraph or heading.
f = font size / body font size. f > 1.1 often indicates a heading.
i = indent. i > 0.02 at a line start often starts a paragraph.
These are evidence, not rules.

## Labels
CONT      continues the current paragraph
PARA      starts a new paragraph
HEAD      heading (do not assign a level)
ARTIFACT  running header, footer, page number, stray OCR
OTHER     caption | footnote | table | list_item | code | quote | equation (set "kind")

## Running outline
{{outline}}

## Previous context
Last committed labels: {{previousLabels}}

## Lines (label every line from {{firstId}} to {{lastId}})
{{lines}}

Return: {"window": {{window}}, "labels": [{"id": "...", "label": "...", "conf": 0.0, "kind": "..."}]}
