"""Per-line geometry in document units (points for PDFs, pixels for images). Only formats that have
pages get a layout; nothing here invents geometry."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field


@dataclass(slots=True)
class LayoutLine:
    page: int
    text: str
    x: float
    y: float
    width: float
    height: float
    pageWidth: float  # noqa: N815
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
    return sorted(lines, key=lambda line: (line.page, round(line.y, 2), round(line.x, 2)))


# PDFs expose no weight axis; subset font names like "ABCDEF+Helvetica-BoldOblique" are all we get.
def is_bold(font_name: str | None) -> bool:
    return bool(font_name) and "bold" in font_name.lower()


def is_italic(font_name: str | None) -> bool:
    lowered = (font_name or "").lower()
    return "italic" in lowered or "oblique" in lowered
