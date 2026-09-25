"""HTTP surface. Callers read 4xx as "will never convert" and 5xx as "retry"."""

from __future__ import annotations

import pytest
from fastapi.testclient import TestClient

from conversion import convert as conversion
from conversion import limits, main


@pytest.fixture
def client(monkeypatch) -> TestClient:
    # The storage client is built on first use, so it can be replaced without AWS configured.
    def no_storage() -> object:
        return object()

    monkeypatch.setattr(main, "storage", no_storage)
    return TestClient(main.app, raise_server_exceptions=False)


def body(**overrides) -> dict:
    payload = {
        "uploadId": "upl_1",
        "sourceKey": "uploads/alice/upl_1.pdf",
        "markdownKey": "uploads/alice/upl_1.md",
        "layoutKey": "uploads/alice/upl_1.layout.json",
        "reportKey": "uploads/alice/upl_1.conversion.json",
        "mediaType": "application/pdf",
        "fileName": "report.pdf",
        "ocr": {"enabled": True, "languages": ["eng"], "maxPages": 200},
        "traceparent": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
    }
    payload.update(overrides)
    return payload


def test_health_answers_without_touching_s3(client):
    assert client.get("/health").json() == {"status": "ok"}


def test_a_successful_conversion_answers_with_keys_and_not_bytes(client, monkeypatch):
    monkeypatch.setattr(
        conversion,
        "convert",
        lambda request, storage, settings: main.ConvertReply(
            markdownKey=request.markdownKey,
            layoutKey=request.layoutKey,
            markdownBytes=1234,
            pages=42,
            producer="markitdown/0.1.14+pdfplumber",
            durationMs=41230,
        ),
    )

    reply = client.post("/convert", json=body())

    assert reply.status_code == 200
    assert reply.json()["markdownKey"] == "uploads/alice/upl_1.md"
    assert reply.json()["pages"] == 42


@pytest.mark.parametrize(
    ("error", "status", "code"),
    [
        (limits.unsupported("no"), 422, "unsupported_format"),
        (limits.too_large("no"), 413, "source_too_large"),
        (limits.timed_out("no"), 504, "conversion_timeout"),
        (limits.failed("no"), 500, "conversion_failed"),
    ],
)
def test_each_failure_answers_with_its_contracted_status_and_code(
    client, monkeypatch, error, status, code
):
    def raise_it(*_args, **_kwargs):
        raise error

    monkeypatch.setattr(conversion, "convert", raise_it)

    reply = client.post("/convert", json=body())

    assert reply.status_code == status
    assert reply.json()["code"] == code
    assert reply.json()["message"]


def test_an_unexpected_bug_answers_500_so_the_run_is_retried(client, monkeypatch):
    def explode(*_args, **_kwargs):
        raise ZeroDivisionError("a bug in this service, not a verdict on the document")

    monkeypatch.setattr(conversion, "convert", explode)

    reply = client.post("/convert", json=body())

    assert reply.status_code == 500
    assert reply.json()["code"] == "conversion_failed"
    # The message is shown to a person, so it must not leak the exception text.
    assert "ZeroDivision" not in reply.json()["message"]


def test_a_malformed_request_is_rejected_before_any_work(client):
    assert client.post("/convert", json={"uploadId": "upl_1"}).status_code == 422
