"""Readiness and stopping, as ProtoStrap checks them.

/healthz answers 200 only when asked for this component by name (any name outside
ProtoStrap), once the documents bucket has answered, and 503 from the first SIGTERM on. The
replica keeps serving for DRAIN_SECONDS, then stops accepting and lets conversions in
progress finish.
"""

from __future__ import annotations

import http.client
import os
import signal
import socket
import subprocess
import sys
import threading
import time
from pathlib import Path

import pytest
import uvicorn
from fastapi.testclient import TestClient

from conversion import config, main
from conversion import convert as conversion

SRC = Path(__file__).resolve().parents[1] / "src"


class FakeStorage:
    def __init__(self, answers: bool = True) -> None:
        self.answers = answers
        self.asked = 0

    def bucket_answers(self) -> bool:
        self.asked += 1
        return self.answers


@pytest.fixture
def storage(monkeypatch) -> FakeStorage:
    fake = FakeStorage()
    monkeypatch.setattr(main, "storage", lambda: fake)
    return fake


@pytest.fixture
def readiness(monkeypatch):
    def make(component: str | None) -> main.Readiness:
        state = main.Readiness(component)
        monkeypatch.setattr(main, "readiness", state)
        return state

    return make


def healthz(host: str):
    return TestClient(main.app).get("/healthz", headers={"host": host})


# Readiness


@pytest.mark.parametrize("host", ["localhost:8090", "127.0.0.1", "conversion", "anything"])
def test_outside_protostrap_it_is_ready_whatever_the_host(storage, readiness, host):
    readiness(None)

    reply = healthz(host)

    assert reply.status_code == 200
    assert reply.json() == {"ready": True}


@pytest.mark.parametrize("host", ["conversion", "conversion:8090"])
def test_under_protostrap_it_is_ready_when_asked_for_by_its_own_name(storage, readiness, host):
    readiness("conversion")

    assert healthz(host).status_code == 200


@pytest.mark.parametrize(
    "host",
    ["api", "api:8080", "localhost", "localhost:8090", "conversion.internal", "xconversion"],
)
def test_under_protostrap_a_check_meant_for_another_service_fails(storage, readiness, host):
    # A stopped replica's overlay address can be handed to another service's replica.
    readiness("conversion")

    reply = healthz(host)

    assert reply.status_code == 503
    assert reply.json()["ready"] is False
    assert storage.asked == 0, "a check for another name must not reach S3"


def test_draining_fails_readiness_even_for_its_own_name(storage, readiness):
    state = readiness("conversion")
    state.draining = True

    reply = healthz("conversion")

    assert reply.status_code == 503
    assert reply.json() == {"ready": False, "draining": True}


def test_draining_fails_readiness_outside_protostrap_too(storage, readiness):
    state = readiness(None)
    state.draining = True

    assert healthz("localhost:8090").status_code == 503


def test_it_is_not_ready_until_the_documents_bucket_answers(storage, readiness):
    readiness("conversion")
    storage.answers = False

    reply = healthz("conversion")

    assert reply.status_code == 503
    assert "bucket" in reply.json()["error"]

    storage.answers = True
    assert healthz("conversion").status_code == 200


def test_once_the_bucket_has_answered_readiness_stops_asking_s3(storage, readiness):
    readiness("conversion")

    for _ in range(5):
        assert healthz("conversion").status_code == 200

    # S3 going away later is a conversion's 5xx to retry, not a reason to pull the replica.
    storage.answers = False
    assert healthz("conversion").status_code == 200
    assert storage.asked == 1


def test_a_storage_client_that_cannot_be_built_is_not_ready(monkeypatch, readiness):
    readiness(None)

    def broken():
        raise RuntimeError("no credentials")

    monkeypatch.setattr(main, "storage", broken)

    assert healthz("localhost").status_code == 503


def test_the_old_health_path_is_gone(storage, readiness):
    readiness(None)

    assert TestClient(main.app).get("/health").status_code == 404


# Stopping


def server(state: main.Readiness, drain_seconds: float, port: int = 0) -> main.DrainingServer:
    return main.DrainingServer(
        uvicorn.Config(main.app, host="127.0.0.1", port=port, log_config=None),
        state,
        drain_seconds,
    )


def test_the_first_sigterm_fails_readiness_at_once_and_keeps_serving_for_the_drain():
    state = main.Readiness("conversion")
    draining = server(state, drain_seconds=0.3)

    draining.handle_exit(signal.SIGTERM, None)

    assert state.draining
    assert not draining.should_exit, "it must keep accepting until the drain is over"

    time.sleep(0.6)

    assert draining.should_exit
    assert not draining.force_exit, "after the drain, requests in progress get to finish"


def test_with_no_drain_it_stops_accepting_at_once():
    state = main.Readiness(None)
    draining = server(state, drain_seconds=0)

    draining.handle_exit(signal.SIGTERM, None)

    assert state.draining
    assert draining.should_exit
    assert not draining.force_exit


def test_a_second_signal_stops_at_once():
    state = main.Readiness("conversion")
    draining = server(state, drain_seconds=60)

    draining.handle_exit(signal.SIGTERM, None)
    draining.handle_exit(signal.SIGTERM, None)

    assert draining.should_exit
    assert draining.force_exit


@pytest.mark.parametrize(
    ("replica", "drain", "expected"),
    [
        (None, None, 0.0),  # A local run stops at once.
        ("conversion-k3f9", None, 10.0),  # ProtoStrap's default, should it not set it.
        ("conversion-k3f9", "3", 3.0),
        (None, "5", 5.0),
        ("conversion-k3f9", "soon", 10.0),
        ("conversion-k3f9", "-1", 0.0),
    ],
)
def test_drain_seconds(monkeypatch, replica, drain, expected):
    for name, value in (("PROTOSTRAP_REPLICA", replica), ("DRAIN_SECONDS", drain)):
        if value is None:
            monkeypatch.delenv(name, raising=False)
        else:
            monkeypatch.setenv(name, value)

    assert config.load().drain_seconds == expected


def test_the_server_outlasts_the_edges_idle_connections_and_lets_conversions_finish():
    settings = config.Settings(timeout_seconds=480, port=8090)

    server_config = main.server_config(main.app, settings)

    # Envoy reuses idle upstream connections for 60 s; uvicorn's default is 5.
    assert server_config.timeout_keep_alive >= 65
    # A conversion checks its budget between steps, so it can run past it by one step.
    assert server_config.timeout_graceful_shutdown >= settings.timeout_seconds + 60
    assert server_config.port == 8090
    assert server_config.host == "0.0.0.0"  # noqa: S104 - every interface, inside the container
    assert server_config.access_log is False


def free_port() -> int:
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def get(port: int, path: str, host: str) -> tuple[int, bytes]:
    connection = http.client.HTTPConnection("127.0.0.1", port, timeout=5)
    try:
        connection.request("GET", path, headers={"Host": host})
        response = connection.getresponse()
        return response.status, response.read()
    finally:
        connection.close()


def test_a_conversion_in_progress_finishes_after_the_drain(storage, monkeypatch):
    """The whole sequence on a real server: SIGTERM, readiness 503 while still serving,
    then new connections refused, and the conversion already running still answers 200."""
    state = main.Readiness("conversion")
    monkeypatch.setattr(main, "readiness", state)
    started = threading.Event()

    def slow_convert(request, storage, settings):
        started.set()
        time.sleep(1.5)
        return main.ConvertReply(
            markdownKey=request.markdownKey,
            layoutKey="",
            markdownBytes=10,
            pages=1,
            producer="test",
            durationMs=1500,
        )

    monkeypatch.setattr(conversion, "convert", slow_convert)

    port = free_port()
    draining = server(state, drain_seconds=0.4, port=port)
    serving = threading.Thread(target=draining.run, daemon=True)
    serving.start()
    deadline = time.monotonic() + 10
    while not draining.started:
        assert time.monotonic() < deadline, "the server didn't start"
        time.sleep(0.02)

    assert get(port, "/healthz", "conversion")[0] == 200

    replies: list[int] = []

    def post() -> None:
        connection = http.client.HTTPConnection("127.0.0.1", port, timeout=10)
        body = (
            '{"uploadId":"upl_1","sourceKey":"uploads/a/upl_1.pdf",'
            '"markdownKey":"uploads/a/upl_1.md","mediaType":"application/pdf"}'
        )
        connection.request(
            "POST",
            "/convert",
            body=body,
            headers={"Host": "conversion", "Content-Type": "application/json"},
        )
        replies.append(connection.getresponse().status)
        connection.close()

    converting = threading.Thread(target=post)
    converting.start()
    assert started.wait(5)

    draining.handle_exit(signal.SIGTERM, None)

    # Still serving during the drain, but no longer ready.
    status, body = get(port, "/healthz", "conversion")
    assert status == 503
    assert b"draining" in body

    # After the drain it stops accepting, while the conversion is still running.
    time.sleep(0.7)
    with pytest.raises(OSError):
        get(port, "/healthz", "conversion")

    converting.join(10)
    serving.join(10)

    assert replies == [200]
    assert not serving.is_alive()


def test_sigterm_drains_and_exits_cleanly(tmp_path):
    """The real signal, in a process started the way the image starts it."""
    port = free_port()
    env = {
        "PATH": os.environ.get("PATH", ""),
        "PYTHONPATH": str(SRC),
        "BUCKET_DOCUMENTS": "x",
        "PROTOSTRAP_COMPONENT": "conversion",
        "PROTOSTRAP_REPLICA": "conversion-test",
        "DRAIN_SECONDS": "1",
        "CONVERSION_PORT": str(port),
        "AWS_REGION": "us-east-1",
    }
    process = subprocess.Popen(  # noqa: S603 - our own interpreter and module
        [sys.executable, "-m", "conversion"],
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    try:
        deadline = time.monotonic() + 20
        while True:
            try:
                status, _ = get(port, "/healthz", "api")
                break
            except OSError:
                assert process.poll() is None, process.stdout.read()
                assert time.monotonic() < deadline, "it didn't start"
                time.sleep(0.1)
        assert status == 503  # Asked for another service by name.

        process.send_signal(signal.SIGTERM)
        time.sleep(0.3)

        status, body = get(port, "/healthz", "conversion")
        assert status == 503
        assert b"draining" in body

        signalled = time.monotonic()
        code = process.wait(10)
        took = time.monotonic() - signalled
    finally:
        if process.poll() is None:
            process.kill()

    output = process.stdout.read()
    assert code == 0, output
    assert 0.4 <= took < 5, f"stopped {took:.1f}s after the signal, with a 1 s drain"
    assert '"msg": "draining' in output
