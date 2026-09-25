"""Wire shapes of ``POST /convert``. camelCase because the callers are .NET."""

from __future__ import annotations

from pydantic import BaseModel, Field


class OcrRequest(BaseModel):
    enabled: bool = True
    languages: list[str] = Field(default_factory=lambda: ["eng"])
    maxPages: int = 200  # noqa: N815


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
    # Empty when the format carries no geometry.
    layoutKey: str = ""  # noqa: N815
    markdownBytes: int = 0  # noqa: N815
    pages: int = 0
    producer: str = ""
    ocr: OcrResult = Field(default_factory=OcrResult)
    warnings: list[str] = Field(default_factory=list)
    durationMs: int = 0  # noqa: N815


class Problem(BaseModel):
    """Error body: callers branch on ``code``; ``message`` is safe to show a user."""

    code: str
    message: str
    detail: str | None = None
