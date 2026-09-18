"""Images: Tesseract OCR, and layout from the OCR boxes (ingest plan §6, §11, §12).

An image has no text layer by definition, so it always takes the OCR path. MarkItDown's own image
handler returns EXIF metadata plus — with an LLM, which this service deliberately does not have —
a caption. Ours returns the recognised text, which is the only thing a segmenter can use.
"""

from __future__ import annotations

from dataclasses import dataclass

from .layout import LayoutDocument, LayoutLine, reading_order

#: Tesseract reports -1 for the boxes it is not reporting a word for.
_NO_CONFIDENCE = -1.0

#: Cap-height to point-size is roughly this; the OCR box has no font, so the line's own height is
#: the only signal available for fontSize. Approximate, and marked as such.
_HEIGHT_TO_FONT_SIZE = 1.0


@dataclass(slots=True)
class ImageText:
    markdown: str
    layout: LayoutDocument
    mean_confidence: float
    words: int


def recognise(path: str, languages: list[str]) -> ImageText:
    """One line per Tesseract ``line_num`` group, with its box and its confidence."""
    # Imported lazily so this module imports without the extra installed — which is what lets
    # the unit tests stub the handlers and run outside the image.
    import pytesseract  # noqa: PLC0415
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
                # Approximated from the line box, and honestly so: there is no font here to read.
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
    """Recognised lines as plain Markdown paragraphs.

    No heading inference here on purpose. Deciding which line is a heading is phases 2–5' job, and
    they do it with the geometry this pass also emits — guessing here would be a second, worse
    segmenter competing with the real one.
    """
    if not lines:
        return ""

    paragraphs: list[list[str]] = [[]]
    previous_bottom: float | None = None

    for line in lines:
        gap = 0.0 if previous_bottom is None else line.y - previous_bottom
        # A gap wider than a line of text is a paragraph break; anything smaller is a wrap.
        if previous_bottom is not None and gap > line.height:
            paragraphs.append([])
        paragraphs[-1].append(line.text)
        previous_bottom = line.y + line.height

    return "\n\n".join(" ".join(p) for p in paragraphs if p).strip() + "\n"
