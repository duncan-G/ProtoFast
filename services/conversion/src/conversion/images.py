"""Images: Tesseract OCR, with layout from the OCR boxes."""

from __future__ import annotations

from dataclasses import dataclass

from .layout import LayoutDocument, LayoutLine, reading_order

# Tesseract's confidence for boxes that are not words.
_NO_CONFIDENCE = -1.0

# OCR boxes carry no font, so fontSize is approximated from the line height.
_HEIGHT_TO_FONT_SIZE = 1.0


@dataclass(slots=True)
class ImageText:
    markdown: str
    layout: LayoutDocument
    mean_confidence: float
    words: int


def recognise(path: str, languages: list[str]) -> ImageText:
    """One layout line per Tesseract ``line_num`` group."""
    import pytesseract  # noqa: PLC0415 - lazy so tests run without it
    from PIL import Image  # noqa: PLC0415

    with Image.open(path) as image:
        width, height = image.size
        data = pytesseract.image_to_data(
            image,
            lang="+".join(languages),
            output_type=pytesseract.Output.DICT,
        )

    groups: dict[tuple[int, int, int, int], list[int]] = {}

    for index, text in enumerate(data.get("text", [])):
        if not str(text).strip():
            continue
        key = (
            data["page_num"][index],
            data["block_num"][index],
            data["par_num"][index],
            data["line_num"][index],
        )
        groups.setdefault(key, []).append(index)

    lines: list[LayoutLine] = []
    confidences: list[float] = []
    words = 0

    for indexes in groups.values():
        text = " ".join(str(data["text"][i]).strip() for i in indexes).strip()
        if not text:
            continue

        left = min(data["left"][i] for i in indexes)
        top = min(data["top"][i] for i in indexes)
        right = max(data["left"][i] + data["width"][i] for i in indexes)
        bottom = max(data["top"][i] + data["height"][i] for i in indexes)
        line_height = float(bottom - top)

        for i in indexes:
            confidence = float(data["conf"][i])
            if confidence > _NO_CONFIDENCE:
                confidences.append(confidence)

        words += len(indexes)

        lines.append(
            LayoutLine(
                page=1,
                text=text,
                x=float(left),
                y=float(top),
                width=float(right - left),
                height=line_height,
                pageWidth=float(width),
                pageHeight=float(height),
                fontSize=round(line_height * _HEIGHT_TO_FONT_SIZE, 1),
            )
        )

    ordered = reading_order(lines)
    mean = round(sum(confidences) / len(confidences), 1) if confidences else 0.0

    return ImageText(
        markdown=to_markdown(ordered),
        layout=LayoutDocument(producer="tesseract", lines=ordered),
        mean_confidence=mean,
        words=words,
    )


def to_markdown(lines: list[LayoutLine]) -> str:
    """Plain paragraphs. Headings are deliberately not guessed; the layout is there for that."""
    if not lines:
        return ""

    paragraphs: list[list[str]] = [[]]
    previous_bottom: float | None = None

    for line in lines:
        gap = 0.0 if previous_bottom is None else line.y - previous_bottom
        # A gap taller than the line is a paragraph break; anything smaller is a wrap.
        if previous_bottom is not None and gap > line.height:
            paragraphs.append([])
        paragraphs[-1].append(line.text)
        previous_bottom = line.y + line.height

    return "\n\n".join(" ".join(p) for p in paragraphs if p).strip() + "\n"
