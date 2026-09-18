"""The conversion sidecar's HTTP surface (ingest plan §9).

One endpoint plus health, internal to Host B's compose network, no published port. It owns no
database, no queue and no state — which is what makes it safe to restart mid-deploy and what would
let it move behind its own queue later without either side of the contract changing.
"""

from __future__ import annotations

import logging

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from . import config
from . import convert as conversion
from .contract import ConvertReply, ConvertRequest, Problem
from .limits import ConversionError
from .storage import Storage

# Keys, sizes, page counts and durations only — never document text, never OCR output, never a
# filename beyond its extension (ingest plan §21).
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
logger = logging.getLogger("conversion")

app = FastAPI(title="ProtoFast conversion", docs_url=None, redoc_url=None, openapi_url=None)

_settings = config.load()
_storage: Storage | None = None


def storage() -> Storage:
    """Built on first use so the module imports without AWS configured, which is what the unit
    tests rely on."""
    global _storage  # noqa: PLW0603 - one process-wide client, deliberately
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


@app.exception_handler(ConversionError)
def on_conversion_error(_: Request, error: ConversionError) -> JSONResponse:
    """The structured error the caller branches on.

    The status carries the retry decision: 4xx means this document will never convert and the run
    should fail now, 5xx means try again. Getting that split right is what keeps one malformed PDF
    from spending five SQS delivery attempts on its way to a DLQ (ingest plan §9).
    """
    logger.warning("conversion failed status=%d code=%s", error.status, error.code)

    return JSONResponse(
        status_code=error.status,
        content=Problem(code=error.code, message=error.message, detail=error.detail).model_dump(),
    )


@app.exception_handler(Exception)
def on_unexpected_error(_: Request, error: Exception) -> JSONResponse:
    """A 500 so the caller retries once. An unexpected exception is a bug in this service, not a
    verdict on the document, and answering 4xx would fail a run that a redeploy might fix."""
    logger.exception("unexpected conversion failure", exc_info=error)

    return JSONResponse(
        status_code=500,
        content=Problem(
            code="conversion_failed",
            message="The document could not be converted.",
            detail=type(error).__name__,
        ).model_dump(),
    )


def main() -> None:  # pragma: no cover - the container entrypoint
    import uvicorn  # noqa: PLC0415 - the server is only needed when actually serving

    # One worker process. Conversion is CPU-bound and memory-hungry, and the container's 1 GB cap
    # is sized for one conversion at a time; a second worker would double the ceiling, not the
    # throughput (ingest plan §25).
    uvicorn.run(app, host="0.0.0.0", port=_settings.port, workers=1, access_log=False)  # noqa: S104


if __name__ == "__main__":  # pragma: no cover
    main()
