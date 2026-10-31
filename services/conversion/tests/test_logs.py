"""Logs leave by one path: one JSON object per line on stdout, which ProtoStrap's collector
takes from Docker. Each line names the component, replica and release, and the trace when
there is one."""

from __future__ import annotations

import json
import logging
import sys

from conversion import logs


def record(**extra) -> logging.LogRecord:
    entry = logging.LogRecord(
        "conversion", logging.INFO, __file__, 1, "converted upload=%s", ("upl_1",), None
    )
    for name, value in extra.items():
        setattr(entry, name, value)
    return entry


def test_a_line_is_one_json_object_with_who_wrote_it():
    formatter = logs.JsonFormatter(
        {"component": "conversion", "replica": "conversion-k3f9", "release": "r42"}
    )

    line = formatter.format(record())

    assert "\n" not in line
    entry = json.loads(line)
    assert entry["msg"] == "converted upload=upl_1"
    assert entry["level"] == "INFO"
    assert entry["logger"] == "conversion"
    assert entry["component"] == "conversion"
    assert entry["replica"] == "conversion-k3f9"
    assert entry["release"] == "r42"
    assert entry["time"].endswith("+00:00")
    assert "trace_id" not in entry


def test_a_line_written_inside_a_trace_carries_it():
    formatter = logs.JsonFormatter({})

    entry = json.loads(
        formatter.format(
            record(otelTraceID="0af7651916cd43dd8448eb211c80319c", otelSpanID="b7ad6b7169203331")
        )
    )

    assert entry["trace_id"] == "0af7651916cd43dd8448eb211c80319c"
    assert entry["span_id"] == "b7ad6b7169203331"


def test_no_trace_is_not_written_as_a_trace():
    # OpenTelemetry's logging instrumentation sets "0" outside a span.
    entry = json.loads(logs.JsonFormatter({}).format(record(otelTraceID="0", otelSpanID="0")))

    assert "trace_id" not in entry


def test_an_exception_stays_on_its_line():
    try:
        raise ValueError("boom")
    except ValueError:
        entry = record()
        entry.exc_info = sys.exc_info()

    line = logs.JsonFormatter({}).format(entry)

    assert "\n" not in line
    assert "ValueError: boom" in json.loads(line)["exception"]


def test_identity_comes_from_protostrap_and_falls_back_locally(monkeypatch):
    monkeypatch.setenv("PROTOSTRAP_COMPONENT", "conversion")
    monkeypatch.setenv("PROTOSTRAP_REPLICA", "conversion-k3f9")
    monkeypatch.setenv("PROTOSTRAP_RELEASE", "r42")
    assert logs.identity() == {
        "component": "conversion",
        "replica": "conversion-k3f9",
        "release": "r42",
    }

    for name in ("PROTOSTRAP_COMPONENT", "PROTOSTRAP_REPLICA", "PROTOSTRAP_RELEASE"):
        monkeypatch.delenv(name)
    local = logs.identity()
    assert local["component"] == "conversion"
    assert local["release"] == "local"
    assert local["replica"]


def test_configure_writes_json_to_stdout_only(capsys):
    root = logging.getLogger()
    saved = (root.handlers[:], root.level)
    try:
        logs.configure()
        logging.getLogger("conversion").info("hello %s", "there")
    finally:
        root.handlers[:], level = saved
        root.setLevel(level)

    captured = capsys.readouterr()
    assert json.loads(captured.out.strip().splitlines()[-1])["msg"] == "hello there"
    assert captured.err == ""
