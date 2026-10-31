from __future__ import annotations

import logging
import os
import re
import sys
import threading
from types import FrameType

import uvicorn
from fastapi import FastAPI, Request
from fastapi.concurrency import run_in_threadpool
from fastapi.responses import JSONResponse

from . import config, logs
from . import convert as conversion
from .contract import ConvertReply, ConvertRequest, Problem
from .limits import ConversionError
from .storage import Storage

# Log keys, sizes and durations only — never document text or file names.
logger = logging.getLogger("conversion")

app = FastAPI(title="ProtoFast conversion", docs_url=None, redoc_url=None, openapi_url=None)

_settings = config.load()
_storage: Storage | None = None

PORT_SUFFIX = re.compile(r":\d+$")

# The caller, document-import, waits 10 minutes for a reply. A conversion checks its budget
# (CONVERSION_TIMEOUT_SECONDS, 480 s) between steps, so it can run past it by the step it is
# in; at shutdown it gets this much longer than its budget to finish.
SHUTDOWN_GRACE_BEYOND_TIMEOUT = 120


def storage() -> Storage:
    # Lazy so the module imports without AWS configured.
    global _storage  # noqa: PLW0603
    if _storage is None:
        _storage = Storage(_settings)
    return _storage


class Readiness:
    """Ready when asked for this component by name, not draining, and once the documents
    bucket has answered. A stopped replica's overlay address can be given to another
    service's replica, so a check meant for another name fails. Outside ProtoStrap (no
    PROTOSTRAP_COMPONENT) the name isn't tested."""

    def __init__(self, component: str | None) -> None:
        self.component = component or None
        self.draining = False
        self.bucket_seen = False

    def host_matches(self, host: str) -> bool:
        if not self.component:
            return True
        return PORT_SUFFIX.sub("", host) == self.component


readiness = Readiness(os.environ.get("PROTOSTRAP_COMPONENT"))


def _bucket_answers() -> bool:
    try:
        return storage().bucket_answers()
    except Exception as error:  # noqa: BLE001 - readiness answers; it never raises
        logger.warning("the documents bucket can't be asked error=%s", type(error).__name__)
        return False


# The spec's `ready: /healthz`. Left out of traces by the image's
# OTEL_PYTHON_FASTAPI_EXCLUDED_URLS, and never logged.
@app.get("/healthz")
async def healthz(request: Request) -> JSONResponse:
    if readiness.draining:
        return JSONResponse({"ready": False, "draining": True}, status_code=503)
    if not readiness.host_matches(request.headers.get("host", "")):
        return JSONResponse({"ready": False}, status_code=503)
    if not readiness.bucket_seen:
        # Asked once: after the bucket has answered, no thread, since conversions may hold
        # them all, and no S3 call per check.
        if not await run_in_threadpool(_bucket_answers):
            return JSONResponse(
                {"ready": False, "error": "the documents bucket doesn't answer"},
                status_code=503,
            )
        readiness.bucket_seen = True
    return JSONResponse({"ready": True})


# A plain def runs on a worker thread, so the event loop answers readiness meanwhile.
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


class DrainingServer(uvicorn.Server):
    """On SIGTERM, fail readiness at once and keep serving for DRAIN_SECONDS, so callers
    notice; then uvicorn's graceful shutdown stops accepting, lets conversions in progress
    finish, and exits. A second signal stops at once.

    Not uvicorn's own handler, which re-raises the signal once stopped: that would end the
    process before the exit handlers that flush the last spans."""

    def __init__(self, config: uvicorn.Config, state: Readiness, drain_seconds: float) -> None:
        super().__init__(config)
        self.readiness = state
        self.drain_seconds = drain_seconds

    def handle_exit(self, sig: int, frame: FrameType | None) -> None:
        if self.readiness.draining:
            self.should_exit = True
            self.force_exit = True
            return
        self.readiness.draining = True
        logger.info("draining drain_seconds=%s signal=%s", self.drain_seconds, sig)
        if self.drain_seconds <= 0:
            self.should_exit = True
            return
        timer = threading.Timer(self.drain_seconds, self._stop_accepting)
        timer.daemon = True
        timer.start()

    def _stop_accepting(self) -> None:
        self.should_exit = True


def server_config(application: FastAPI, settings: config.Settings) -> uvicorn.Config:
    return uvicorn.Config(
        application,
        host="0.0.0.0",  # noqa: S104 - every interface, inside the container
        port=settings.port,
        log_config=None,
        access_log=False,
        server_header=False,
        # Longer than Envoy's 60 s idle timeout; uvicorn's default is 5.
        timeout_keep_alive=65,
        # After the drain, conversions in progress get this long. The spec's `stop` must
        # cover DRAIN_SECONDS plus this.
        timeout_graceful_shutdown=settings.timeout_seconds + SHUTDOWN_GRACE_BEYOND_TIMEOUT,
    )


def main() -> None:
    logs.configure()
    try:
        config.check(_settings)
    except config.SettingsError as error:
        logger.error("can't start: %s", error)  # noqa: TRY400 - the reason is the message
        sys.exit(1)

    logger.info("starting port=%d", _settings.port)
    # One process per replica: ProtoStrap scales replicas, not workers, and forked workers
    # would need extra setup to export telemetry.
    DrainingServer(server_config(app, _settings), readiness, _settings.drain_seconds).run()


if __name__ == "__main__":  # pragma: no cover
    main()
