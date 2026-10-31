"""Logs leave by one path: one JSON object per line on stdout, which ProtoStrap's collector
takes from Docker (and Aspire shows under `aspire run`). The image turns OpenTelemetry's OTLP
log handler off, so nothing is sent twice.

Log keys, sizes and durations only: never document text or file names."""

from __future__ import annotations

import json
import logging
import os
import socket
import sys
from datetime import UTC, datetime


def identity() -> dict[str, str]:
    """The component, replica and release ProtoStrap names, on every line."""
    return {
        "component": os.environ.get("PROTOSTRAP_COMPONENT") or "conversion",
        "replica": os.environ.get("PROTOSTRAP_REPLICA") or socket.gethostname(),
        "release": os.environ.get("PROTOSTRAP_RELEASE") or "local",
    }


class JsonFormatter(logging.Formatter):
    """With OTEL_PYTHON_LOG_CORRELATION=true, OpenTelemetry's logging instrumentation puts
    the trace on each record, and the line carries it."""

    def __init__(self, who: dict[str, str]) -> None:
        super().__init__()
        self._who = who

    def format(self, record: logging.LogRecord) -> str:
        entry: dict[str, object] = {
            "time": datetime.fromtimestamp(record.created, UTC).isoformat(),
            "level": record.levelname,
            "msg": record.getMessage(),
            "logger": record.name,
            **self._who,
        }
        trace_id = getattr(record, "otelTraceID", None)
        if trace_id and trace_id != "0":
            entry["trace_id"] = trace_id
            entry["span_id"] = getattr(record, "otelSpanID", None)
        if record.exc_info:
            entry["exception"] = self.formatException(record.exc_info)
        return json.dumps(entry)


def configure() -> None:
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JsonFormatter(identity()))
    logging.basicConfig(level=logging.INFO, handlers=[handler], force=True)
