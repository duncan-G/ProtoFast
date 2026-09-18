"""S3 access: read the source, write the Markdown, the layout and the report (ingest plan §13).

Only keys cross the wire between the worker and this service; the bytes go straight to and from
S3. The converter holds no credentials of its own — the instance role over IMDS in production,
LocalStack's throwaway pair in development.
"""

from __future__ import annotations

import hashlib
import json
from typing import Any

import boto3
from botocore.config import Config
from botocore.exceptions import ClientError

from .config import Settings

#: The stamp that makes conversion idempotent. A second conversion of the same source finds this
#: on the existing Markdown object and returns without doing the work again (ingest plan N5, C10).
SOURCE_HASH_METADATA = "pf-source-hash"

#: Mirrors what ``S3ArtifactStore`` writes, so an object this service produced is indistinguishable
#: from one a phase produced as far as the store's own idempotency check is concerned.
IDEMPOTENCY_METADATA = "pf-idempotency"


class Storage:
    def __init__(self, settings: Settings, client: Any | None = None) -> None:
        self._settings = settings
        self._s3 = client or _client(settings)

    @property
    def bucket(self) -> str:
        return self._settings.bucket

    def content_length(self, key: str) -> int | None:
        """The object's size without downloading it — the §7.3 backstop's whole point."""
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
        """Streams the object to a file. Conversion is file-based, and so is every library here."""
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
        """The hash stamped on an object we wrote before, or None when there is no such object."""
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
            # LocalStack serves one host for every bucket, so virtual-host addressing does not
            # resolve there; real S3 is unaffected by the explicit choice.
            s3={"addressing_style": "path" if settings.s3_endpoint else "auto"},
            retries={"max_attempts": 3, "mode": "standard"},
        ),
    )


def sha256_file(path: str) -> str:
    """Streamed, because the whole point of the memory cap is not holding the file twice."""
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return f"sha256:{digest.hexdigest()}"


def sha256_text(text: str) -> str:
    return f"sha256:{hashlib.sha256(text.encode('utf-8')).hexdigest()}"
