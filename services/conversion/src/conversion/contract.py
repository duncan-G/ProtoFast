"""The wire shapes of ``POST /convert`` (ingest plan §9, appendix B).

Field names are camelCase because the caller is .NET with web-default JSON options. They are a
contract with ``ConversionContract.cs``, not a style choice, so they are spelled out rather than
generated from the Python attribute names.
"""

from __future__ import annotations

from pydantic import BaseModel, Field


class OcrRequest(BaseModel):
    enabled: bool = True
    languages: list[str] = Field(default_factory=lambda: ["eng"])
    maxPages: int = 200  # noqa: N815 - camelCase is the wire shape


class ConvertRequest(BaseModel):
    uploadId: str  # noqa: N815
    sourceKey: str  # noqa: N815
    markdownKey: str  # noqa: N815
    layoutKey: str = ""  # noqa: N815
    reportKey: str = ""  # noqa: N815
    mediaType: str  # noqa: N815
    fileName: str = ""  # noqa: N815
    ocr: OcrRequest = Field(default_factory=OcrRequest)
    traceparent: str | None = None


class OcrResult(BaseModel):
    applied: bool = False
    engine: str = ""
    pagesOcred: int = 0  # noqa: N815
    meanConfidence: float = 0.0  # noqa: N815


class ConvertReply(BaseModel):
    markdownKey: str  # noqa: N815
    #: Empty when the format carries no geometry — an honest absence the pipeline already handles.
    layoutKey: str = ""  # noqa: N815
    markdownBytes: int = 0  # noqa: N815
    pages: int = 0
    producer: str = ""
    ocr: OcrResult = Field(default_factory=OcrResult)
    warnings: list[str] = Field(default_factory=list)
    durationMs: int = 0  # noqa: N815


class Problem(BaseModel):
    """The error body. ``code`` is what the caller branches on; ``message`` is shown to a person."""

    code: str
    message: str
    detail: str | None = None
