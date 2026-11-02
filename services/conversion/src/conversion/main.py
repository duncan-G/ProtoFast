"""Drains the document-conversion queue one message at a time. Each conversion's outcome goes
back to document-import on the document-upload queue, inside the import message it came with."""

from __future__ import annotations

import logging
import signal
import sys
from collections.abc import Callable, Iterator
from contextlib import contextmanager
from types import FrameType
from typing import Any

from opentelemetry import trace
from pydantic import ValidationError

from . import config, logs
from . import convert as conversion
from .contract import ConvertReply, ConvertRequest
from .limits import ConversionError
from .sqs import Message, Queues
from .storage import Storage

# Log keys, sizes and durations only — never document text or file names.
logger = logging.getLogger("conversion")
tracer = trace.get_tracer("conversion")

Convert = Callable[[ConvertRequest, Storage, config.Settings], ConvertReply]


class Stopping(BaseException):  # noqa: N818 - not an error; nothing may catch it as one
    """SIGTERM during a conversion. Its message goes back on the queue for another replica."""


class Worker:
    def __init__(
        self,
        queues: Queues,
        storage: Storage,
        settings: config.Settings,
        convert: Convert = conversion.convert,
    ) -> None:
        self.queues = queues
        self.storage = storage
        self.settings = settings
        self.convert = convert
        self.stopping = False
        self._interruptible = False

    def run(self) -> None:
        while not self.stopping:
            for message in self.queues.receive():
                self.handle(message)

    def on_signal(self, sig: int, _frame: FrameType | None) -> None:
        """A conversion is cut short, so a stop never waits for one; the outcome being sent
        is not, so document-import never gets it twice."""
        if self.stopping:
            return
        self.stopping = True
        logger.info("stopping signal=%s", sig)
        if self._interruptible:
            self._interruptible = False
            raise Stopping

    def handle(self, message: Message) -> None:
        with tracer.start_as_current_span(
            "document-conversion process",
            context=message.trace,
            kind=trace.SpanKind.CONSUMER,
            attributes={"messaging.system": "aws_sqs", "messaging.operation.type": "process"},
        ):
            try:
                request = ConvertRequest.model_validate_json(message.body)
            except ValidationError as error:
                # Never forwarded, so never deleted: it goes to the dead-letter queue to be read.
                logger.error("unreadable conversion message errors=%d", error.error_count())  # noqa: TRY400
                return

            try:
                with self._cut_short():
                    outcome = self._convert(request, message)
            except Stopping:
                self.queues.release(message)
                logger.info("conversion stopped upload=%s; back on the queue", request.uploadId)
                return

            if outcome is None:
                return
            self.queues.send_import({**request.importRequest, "conversion": outcome})
            self.queues.delete(message)

    def _convert(self, request: ConvertRequest, message: Message) -> dict[str, Any] | None:
        """The outcome document-import reads, or None to leave the message for redelivery."""
        logger.info(
            "converting upload=%s media_type=%s source=%s",
            request.uploadId,
            request.mediaType,
            request.sourceKey,
        )
        try:
            reply = self.convert(request, self.storage, self.settings)
        except ConversionError as error:
            # A verdict on the document: converting it again would fail the same way.
            logger.warning("conversion failed status=%d code=%s", error.status, error.code)
            return {"error": error.code}
        except Exception:
            if not self.queues.is_last_delivery(message):
                logger.exception("conversion failed upload=%s; redelivered", request.uploadId)
                return None
            logger.exception("conversion failed upload=%s for the last time", request.uploadId)
            return {"error": "conversion_failed"}

        logger.info(
            "converted upload=%s pages=%d markdown_bytes=%d ocr=%s duration_ms=%d",
            request.uploadId,
            reply.pages,
            reply.markdownBytes,
            reply.ocr.applied,
            reply.durationMs,
        )
        return {"markdownKey": reply.markdownKey}

    @contextmanager
    def _cut_short(self) -> Iterator[None]:
        self._interruptible = not self.stopping
        try:
            if self.stopping:
                raise Stopping
            yield
        finally:
            self._interruptible = False


def main() -> None:
    logs.configure()
    settings = config.load()
    try:
        config.check(settings)
    except config.SettingsError as error:
        logger.error("can't start: %s", error)  # noqa: TRY400 - the reason is the message
        sys.exit(1)

    worker = Worker(Queues(settings), Storage(settings), settings)
    signal.signal(signal.SIGTERM, worker.on_signal)
    signal.signal(signal.SIGINT, worker.on_signal)
    logger.info("draining the document-conversion queue")
    worker.run()
    logger.info("stopped")


if __name__ == "__main__":  # pragma: no cover
    main()
