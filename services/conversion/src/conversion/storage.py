from __future__ import annotations

import hashlib
import json
from typing import Any

import boto3
from botocore.config import Config
from botocore.exceptions import ClientError

from .config import Settings

# Stamped on every output; a re-conversion of the same source finds it and skips the work.
SOURCE_HASH_METADATA = "pf-source-hash"
IDEMPOTENCY_METADATA = "pf-idempotency"


class Storage:
    def __init__(self, settings: Settings, client: Any | None = None) -> None:
        self._settings = settings
        self._s3 = client or _client(settings)

    @property
    def bucket(self) -> str:
        return self._settings.bucket

    def content_length(self, key: str) -> int | None:
        head = self.head(key)
        return None if head is None else int(head["ContentLength"])

    def head(self, key: str) -> dict | None:
        try:
            return self._s3.head_object(Bucket=self.bucket, Key=key)
        except ClientError as error:
            if error.response.get("Error", {}).get("Code") in {"404", "NoSuchKey", "NotFound"}:
                return None
            raise

    def download(self, key: str, destination: str) -> str:
        self._s3.download_file(self.bucket, key, destination)
        return destination

    def get_text(self, key: str) -> str:
        response = self._s3.get_object(Bucket=self.bucket, Key=key)
        return response["Body"].read().decode("utf-8", "replace")

    def put_text(
        self,
        key: str,
        text: str,
        content_type: str,
        source_hash: str,
        idempotency_key: str,
    ) -> int:
        body = text.encode("utf-8")
        self._s3.put_object(
            Bucket=self.bucket,
            Key=key,
            Body=body,
            ContentType=content_type,
            Metadata={
                SOURCE_HASH_METADATA: source_hash,
                IDEMPOTENCY_METADATA: idempotency_key,
            },
        )
        return len(body)

    def put_json(self, key: str, value: Any, source_hash: str, idempotency_key: str) -> int:
        return self.put_text(
            key,
            json.dumps(value, ensure_ascii=False, separators=(",", ":")),
            "application/json",
            source_hash,
            idempotency_key,
        )

    def existing_source_hash(self, key: str) -> str | None:
        head = self.head(key)
        if head is None:
            return None
        return head.get("Metadata", {}).get(SOURCE_HASH_METADATA)


def _client(settings: Settings) -> Any:
    return boto3.client(
        "s3",
        endpoint_url=settings.s3_endpoint,
        region_name=settings.region,
        config=Config(
            # LocalStack serves every bucket from one host, so virtual-host addressing fails there.
            s3={"addressing_style": "path" if settings.s3_endpoint else "auto"},
            retries={"max_attempts": 3, "mode": "standard"},
        ),
    )


def sha256_file(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return f"sha256:{digest.hexdigest()}"
