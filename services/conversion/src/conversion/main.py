from __future__ import annotations

import logging

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from . import config
from . import convert as conversion
from .contract import ConvertReply, ConvertRequest, Problem
from .limits import ConversionError
from .storage import Storage

# Log keys, sizes and durations only — never document text or file names.
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
logger = logging.getLogger("conversion")

app = FastAPI(title="ProtoFast conversion", docs_url=None, redoc_url=None, openapi_url=None)

_settings = config.load()
_storage: Storage | None = None


def storage() -> Storage:
    # Lazy so the module imports without AWS configured.
    global _storage  # noqa: PLW0603
    if _storage is None:
        _storage = Storage(_settings)
    return _storage


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/convert", response_model=ConvertReply)
def post_convert(request: ConvertRequest) -> ConvertReply:
    logger.info(
        "converting upload=%s media_type=%s source=%s",
        request.uploadId,
        request.mediaType,
        request.sourceKey,
    )

    reply = conversion.convert(request, storage(), _settings)

    logger.info(
        "converted upload=%s pages=%d markdown_bytes=%d ocr=%s duration_ms=%d",
        request.uploadId,
        reply.pages,
        reply.markdownBytes,
        reply.ocr.applied,
        reply.durationMs,
    )

    return reply


# 4xx means the document will never convert; 5xx means the caller may retry.
@app.exception_handler(ConversionError)
def on_conversion_error(_: Request, error: ConversionError) -> JSONResponse:
    logger.warning("conversion failed status=%d code=%s", error.status, error.code)

    return JSONResponse(
        status_code=error.status,
        content=Problem(code=error.code, message=error.message, detail=error.detail).model_dump(),
    )


@app.exception_handler(Exception)
def on_unexpected_error(_: Request, error: Exception) -> JSONResponse:
    logger.exception("unexpected conversion failure", exc_info=error)

    return JSONResponse(
        status_code=500,
        content=Problem(
            code="conversion_failed",
            message="The document could not be converted.",
            detail=type(error).__name__,
        ).model_dump(),
    )


def main() -> None:  # pragma: no cover
    import uvicorn  # noqa: PLC0415

    # One worker: the container's memory cap is sized for one conversion at a time.
    uvicorn.run(app, host="0.0.0.0", port=_settings.port, workers=1, access_log=False)  # noqa: S104


if __name__ == "__main__":  # pragma: no cover
    main()
