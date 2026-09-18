"""Per-line layout in the schema ``LayoutInput.cs`` already defines (ingest plan §11).

MarkItDown produces Markdown; it does not produce geometry — and geometry is what
``MarkdownExtractor`` and ``LayoutJoiner`` use to tell a heading from a bold line. So layout is
ours to extract alongside the conversion, in *document* units (points, pixels), which is what the
.NET side expects to normalise itself.

Nothing here invents geometry. A format with no pages gets no layout file at all: emitting a
synthetic one with made-up font sizes would feed the joiner confident nonsense, which is worse for
the pipeline than an honest absence it already handles.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass, field


@dataclass(slots=True)
class LayoutLine:
    """One line, in the exact JSON shape ``RawLayoutLine`` deserialises."""

    page: int
    text: str
    x: float
    y: float
    width: float
    height: float
    pageWidth: float  # noqa: N815 - the wire shape is camelCase; it is a contract, not a style
    pageHeight: float  # noqa: N815
    fontSize: float = 0.0  # noqa: N815
    bold: bool = False
    italic: bool = False
    column: int = 0


@dataclass(slots=True)
class LayoutDocument:
    producer: str
    lines: list[LayoutLine] = field(default_factory=list)

    def to_json(self) -> dict:
        return {"producer": self.producer, "lines": [asdict(line) for line in self.lines]}


def reading_order(lines: list[LayoutLine]) -> list[LayoutLine]:
    """Page, then top, then left.

    ``LayoutJoiner`` aligns layout lines to Markdown lines by text, walking both in order. Emitting
    out of order does not break the join but makes it quadratic, so the sort is here rather than
    left to the consumer.
    """
    return sorted(lines, key=lambda line: (line.page, round(line.y, 2), round(line.x, 2)))


def is_bold(font_name: str | None) -> bool:
    """Inferred from the font name, which is all a PDF actually tells us.

    Embedded subsets are named like ``ABCDEF+Helvetica-BoldOblique``, so this is a substring test
    on purpose: there is no weight axis to read in a Type 1 or TrueType font reference.
    """
    return bool(font_name) and "bold" in font_name.lower()


def is_italic(font_name: str | None) -> bool:
    lowered = (font_name or "").lower()
    return "italic" in lowered or "oblique" in lowered
