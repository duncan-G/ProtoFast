"""The two queues: document-conversion, which this worker drains, and document-upload, where
each outcome goes back to document-import. The trace travels in the `traceparent` and
`tracestate` message attributes, as ProtoFast.Storage's SqsMessageQueue sends and reads them."""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any

import boto3
from botocore.config import Config
from opentelemetry import propagate
from opentelemetry.context import Context
from opentelemetry.instrumentation.utils import suppress_instrumentation

from .config import Settings

TRACE_ATTRIBUTES = ("traceparent", "tracestate")

# Long polls, so an idle replica asks about three times a minute; within the spec's `stop`.
WAIT_SECONDS = 20


@dataclass(frozen=True, slots=True)
class Message:
    body: str
    receipt_handle: str
    receive_count: int
    trace: Context


class Queues:
    def __init__(self, settings: Settings, client: Any | None = None) -> None:
        self._settings = settings
        self._sqs = client or _client(settings)
        self._max_receives: int | None = None
        self._max_receives_read = False

    def receive(self) -> list[Message]:
        """One message at most: a replica converts one document at a time."""
        # Empty polls aren't traced; the conversion's own span covers a message.
        with suppress_instrumentation():
            response = self._sqs.receive_message(
                QueueUrl=self._settings.conversions_queue_url,
                MaxNumberOfMessages=1,
                WaitTimeSeconds=WAIT_SECONDS,
                MessageAttributeNames=list(TRACE_ATTRIBUTES),
                MessageSystemAttributeNames=["ApproximateReceiveCount"],
            )
        return [
            Message(
                body=raw["Body"],
                receipt_handle=raw["ReceiptHandle"],
                receive_count=int(raw.get("Attributes", {}).get("ApproximateReceiveCount", "1")),
                trace=propagate.extract(_carrier(raw.get("MessageAttributes", {}))),
            )
            for raw in response.get("Messages", [])
        ]

    def is_last_delivery(self, message: Message) -> bool:
        """After this one the message goes to the dead-letter queue, unseen by document-import."""
        limit = self.max_receives()
        return limit is not None and message.receive_count >= limit

    def max_receives(self) -> int | None:
        if not self._max_receives_read:
            with suppress_instrumentation():
                attributes = self._sqs.get_queue_attributes(
                    QueueUrl=self._settings.conversions_queue_url,
                    AttributeNames=["RedrivePolicy"],
                ).get("Attributes", {})
            policy = attributes.get("RedrivePolicy")
            self._max_receives = int(json.loads(policy)["maxReceiveCount"]) if policy else None
            self._max_receives_read = True
        return self._max_receives

    def send_import(self, body: dict[str, Any]) -> None:
        carrier: dict[str, str] = {}
        propagate.inject(carrier)
        self._sqs.send_message(
            QueueUrl=self._settings.imports_queue_url,
            MessageBody=json.dumps(body, ensure_ascii=False, separators=(",", ":")),
            MessageAttributes={
                name: {"DataType": "String", "StringValue": value}
                for name, value in carrier.items()
                if name in TRACE_ATTRIBUTES
            },
        )

    def delete(self, message: Message) -> None:
        self._sqs.delete_message(
            QueueUrl=self._settings.conversions_queue_url, ReceiptHandle=message.receipt_handle
        )

    def release(self, message: Message) -> None:
        """Back on the queue at once, for another replica."""
        self._sqs.change_message_visibility(
            QueueUrl=self._settings.conversions_queue_url,
            ReceiptHandle=message.receipt_handle,
            VisibilityTimeout=0,
        )


def _carrier(attributes: dict[str, Any]) -> dict[str, str]:
    return {
        name: value["StringValue"]
        for name, value in attributes.items()
        if name in TRACE_ATTRIBUTES and "StringValue" in value
    }


def _client(settings: Settings) -> Any:
    """The default credential chain, as the S3 client's. boto3 reads AWS_ENDPOINT_URL_SQS."""
    return boto3.client(
        "sqs",
        region_name=settings.region,
        # Longer than the long poll, or every empty receive would time out.
        config=Config(
            connect_timeout=5,
            read_timeout=WAIT_SECONDS + 10,
            retries={"max_attempts": 3, "mode": "standard"},
        ),
    )
