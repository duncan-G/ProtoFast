"""The queue worker. Each message's outcome goes back to document-import inside its own import
message: the Markdown's key, or the error code that says the document will never convert. A
failure that isn't a verdict on the document is redelivered, and reported on its last
delivery. SIGTERM puts the message in hand back on the queue at once."""

from __future__ import annotations

import json
import os
import signal
import threading

import pytest
from opentelemetry.context import Context

from conversion import config, limits, main
from conversion.contract import ConvertReply
from conversion.sqs import Message, Queues

IMPORT = {
    "uploadId": "upl_1",
    "userId": "alice",
    "storageKey": "uploads/alice/upl_1.pdf",
    "fileName": "report.pdf",
    "mediaType": "application/pdf",
    "fileExtension": ".pdf",
}


def body(**overrides) -> str:
    payload = {
        "uploadId": "upl_1",
        "sourceKey": "uploads/alice/upl_1.pdf",
        "markdownKey": "uploads/alice/upl_1.md",
        "layoutKey": "uploads/alice/upl_1.layout.json",
        "reportKey": "uploads/alice/upl_1.conversion.json",
        "mediaType": "application/pdf",
        "importRequest": IMPORT,
    }
    payload.update(overrides)
    return json.dumps(payload)


def message(text: str | None = None, receive_count: int = 1) -> Message:
    return Message(
        body=text or body(), receipt_handle="rh-1", receive_count=receive_count, trace=Context()
    )


class FakeQueues:
    def __init__(self, max_receives: int | None = 5) -> None:
        self.max_receives = max_receives
        self.sent: list[dict] = []
        self.deleted: list[str] = []
        self.released: list[str] = []
        self.waiting: list[Message] = []

    def receive(self) -> list[Message]:
        return [self.waiting.pop(0)] if self.waiting else []

    def is_last_delivery(self, msg: Message) -> bool:
        return self.max_receives is not None and msg.receive_count >= self.max_receives

    def send_import(self, payload: dict) -> None:
        self.sent.append(payload)

    def delete(self, msg: Message) -> None:
        self.deleted.append(msg.receipt_handle)

    def release(self, msg: Message) -> None:
        self.released.append(msg.receipt_handle)


def reply(request) -> ConvertReply:
    return ConvertReply(markdownKey=request.markdownKey, markdownBytes=12, pages=3, durationMs=5)


def worker(queues: FakeQueues, convert) -> main.Worker:
    return main.Worker(queues, storage=object(), settings=config.Settings(), convert=convert)


def test_a_conversion_sends_the_import_back_with_the_markdown_key_then_deletes_its_message():
    queues = FakeQueues()

    worker(queues, lambda request, *_: reply(request)).handle(message())

    assert queues.sent == [{**IMPORT, "conversion": {"markdownKey": "uploads/alice/upl_1.md"}}]
    assert queues.deleted == ["rh-1"]


@pytest.mark.parametrize(
    ("error", "code"),
    [
        (limits.unsupported("no"), "unsupported_format"),
        (limits.too_large("no"), "source_too_large"),
        (limits.timed_out("no"), "conversion_timeout"),
        (limits.failed("no"), "conversion_failed"),
    ],
)
def test_a_verdict_on_the_document_is_sent_back_at_once(error, code):
    queues = FakeQueues()

    def refuse(*_):
        raise error

    worker(queues, refuse).handle(message())

    assert queues.sent == [{**IMPORT, "conversion": {"error": code}}]
    assert queues.deleted == ["rh-1"]


def test_an_unexpected_failure_is_left_for_redelivery():
    queues = FakeQueues()

    def explode(*_):
        raise ConnectionError("S3 blinked")

    worker(queues, explode).handle(message(receive_count=2))

    assert queues.sent == []
    assert queues.deleted == []
    assert queues.released == []


def test_on_its_last_delivery_an_unexpected_failure_is_reported_rather_than_dead_lettered():
    queues = FakeQueues(max_receives=5)

    def explode(*_):
        raise ZeroDivisionError("a bug")

    worker(queues, explode).handle(message(receive_count=5))

    assert queues.sent == [{**IMPORT, "conversion": {"error": "conversion_failed"}}]
    assert queues.deleted == ["rh-1"]


def test_without_a_dead_letter_queue_an_unexpected_failure_is_always_redelivered():
    queues = FakeQueues(max_receives=None)

    def explode(*_):
        raise ZeroDivisionError("a bug")

    worker(queues, explode).handle(message(receive_count=50))

    assert queues.sent == []


def test_an_unreadable_message_is_left_for_the_dead_letter_queue():
    queues = FakeQueues()

    worker(queues, lambda request, *_: reply(request)).handle(message('{"uploadId": "upl_1"}'))

    assert queues.sent == []
    assert queues.deleted == []


def test_sigterm_during_a_conversion_puts_its_message_back_and_stops():
    queues = FakeQueues()
    queues.waiting = [message(), message()]
    converting = threading.Event()
    subject: main.Worker

    def interrupted(*_):
        converting.set()
        os.kill(os.getpid(), signal.SIGTERM)
        threading.Event().wait(5)
        raise AssertionError("the signal didn't cut the conversion short")

    subject = worker(queues, interrupted)
    previous = signal.signal(signal.SIGTERM, subject.on_signal)
    try:
        subject.run()
    finally:
        signal.signal(signal.SIGTERM, previous)

    assert converting.is_set()
    assert queues.released == ["rh-1"]
    assert queues.sent == []
    assert queues.deleted == []
    # The second message is never taken.
    assert len(queues.waiting) == 1


def test_sigterm_while_sending_the_outcome_lets_it_finish():
    queues = FakeQueues()
    subject = worker(queues, lambda request, *_: reply(request))
    sending = queues.send_import

    def send_then_signal(payload: dict) -> None:
        subject.on_signal(signal.SIGTERM, None)
        sending(payload)

    queues.send_import = send_then_signal

    subject.handle(message())

    assert len(queues.sent) == 1
    assert queues.deleted == ["rh-1"]
    assert subject.stopping


def test_a_message_taken_after_sigterm_goes_straight_back():
    queues = FakeQueues()
    called = []
    subject = worker(queues, lambda *args: called.append(args))
    subject.stopping = True

    subject.handle(message())

    assert called == []
    assert queues.released == ["rh-1"]


class RecordingSqs:
    def __init__(self, messages: list[dict] | None = None, policy: str | None = None) -> None:
        self.messages = messages or []
        self.policy = policy
        self.calls: list[tuple[str, dict]] = []

    def receive_message(self, **kwargs):
        self.calls.append(("receive_message", kwargs))
        return {"Messages": self.messages}

    def get_queue_attributes(self, **kwargs):
        self.calls.append(("get_queue_attributes", kwargs))
        return {"Attributes": {"RedrivePolicy": self.policy}} if self.policy else {}

    def send_message(self, **kwargs):
        self.calls.append(("send_message", kwargs))
        return {}


SETTINGS = config.Settings(
    conversions_queue_url="http://sqs/document-conversion",
    imports_queue_url="http://sqs/document-upload",
)


def test_a_received_message_carries_its_receive_count_and_trace():
    sqs = RecordingSqs(
        [
            {
                "Body": body(),
                "ReceiptHandle": "rh-9",
                "Attributes": {"ApproximateReceiveCount": "3"},
                "MessageAttributes": {
                    "traceparent": {
                        "DataType": "String",
                        "StringValue": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
                    }
                },
            }
        ]
    )

    [received] = Queues(SETTINGS, client=sqs).receive()

    from opentelemetry import trace  # noqa: PLC0415

    parent = trace.get_current_span(received.trace).get_span_context()
    assert received.receipt_handle == "rh-9"
    assert received.receive_count == 3
    assert format(parent.trace_id, "032x") == "0af7651916cd43dd8448eb211c80319c"
    name, kwargs = sqs.calls[0]
    assert kwargs["QueueUrl"] == "http://sqs/document-conversion"
    assert kwargs["MaxNumberOfMessages"] == 1


@pytest.mark.parametrize(
    ("policy", "count", "last"),
    [
        ('{"deadLetterTargetArn":"arn","maxReceiveCount":5}', 5, True),
        ('{"deadLetterTargetArn":"arn","maxReceiveCount":"5"}', 4, False),
        (None, 99, False),
    ],
)
def test_the_last_delivery_is_the_redrive_policys_receive_limit(policy, count, last):
    sqs = RecordingSqs(policy=policy)
    queues = Queues(SETTINGS, client=sqs)

    assert queues.is_last_delivery(message(receive_count=count)) is last
    queues.is_last_delivery(message(receive_count=count))
    assert [name for name, _ in sqs.calls] == ["get_queue_attributes"]


def test_the_outcome_goes_to_the_upload_queue():
    sqs = RecordingSqs()

    Queues(SETTINGS, client=sqs).send_import({"uploadId": "upl_1", "conversion": {"error": "x"}})

    [(name, kwargs)] = sqs.calls
    assert name == "send_message"
    assert kwargs["QueueUrl"] == "http://sqs/document-upload"
    assert json.loads(kwargs["MessageBody"]) == {"uploadId": "upl_1", "conversion": {"error": "x"}}
