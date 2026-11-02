from __future__ import annotations

import os
from dataclasses import dataclass, field

MEBIBYTE = 1024 * 1024


def _int(name: str, default: int) -> int:
    raw = os.environ.get(name)
    if not raw:
        return default
    try:
        return int(raw)
    except ValueError:
        return default


def _list(name: str, default: list[str]) -> list[str]:
    raw = os.environ.get(name, "")
    values = [item.strip() for item in raw.split(",") if item.strip()]
    return values or default


@dataclass(frozen=True, slots=True)
class Settings:
    # The documents bucket, from the spec's `uses: [documents]` (or the AppHost's reference).
    bucket: str = field(default_factory=lambda: os.environ.get("BUCKET_DOCUMENTS", ""))

    # The queue it drains, from its scale signal, and the one each outcome goes back on.
    conversions_queue_url: str = field(
        default_factory=lambda: os.environ.get("QUEUE_DOCUMENT_CONVERSION_URL", "")
    )
    imports_queue_url: str = field(
        default_factory=lambda: os.environ.get("QUEUE_DOCUMENT_UPLOAD_URL", "")
    )

    # LocalStack under `aspire run`; unset in an account, where boto3 resolves real S3 and
    # gets the role's credentials from AWS_CONTAINER_CREDENTIALS_RELATIVE_URI. boto3 reads
    # the endpoint itself; it is read here only to choose path-style addressing.
    s3_endpoint: str | None = field(
        default_factory=lambda: (
            os.environ.get("AWS_ENDPOINT_URL_S3") or os.environ.get("AWS_ENDPOINT_URL") or None
        )
    )

    # botocore reads AWS_DEFAULT_REGION, not AWS_REGION, which is what ProtoStrap sets.
    region: str | None = field(
        default_factory=lambda: (
            os.environ.get("AWS_REGION") or os.environ.get("AWS_DEFAULT_REGION") or None
        )
    )

    # Backstop for the upload policy's limit (SourceFormats.DefaultMaxBytes).
    max_source_bytes: int = field(
        default_factory=lambda: _int("CONVERSION_MAX_SOURCE_BYTES", 10 * MEBIBYTE)
    )

    max_output_bytes: int = field(
        default_factory=lambda: _int("CONVERSION_MAX_OUTPUT_BYTES", 25 * MEBIBYTE)
    )

    # Well inside the queue's 15-minute visibility, so a slow conversion isn't delivered twice.
    timeout_seconds: int = field(default_factory=lambda: _int("CONVERSION_TIMEOUT_SECONDS", 480))

    ocr_languages: list[str] = field(
        default_factory=lambda: _list("CONVERSION_OCR_LANGUAGES", ["eng"])
    )

    # Percent. Below it the reply carries a warning rather than failing.
    ocr_min_confidence: float = field(
        default_factory=lambda: float(os.environ.get("CONVERSION_OCR_MIN_CONFIDENCE", "60"))
    )

    max_ocr_pages: int = field(default_factory=lambda: _int("CONVERSION_MAX_OCR_PAGES", 200))

    max_pages: int = field(default_factory=lambda: _int("CONVERSION_MAX_PAGES", 2000))


class SettingsError(Exception):
    """A setting the worker can't start without is missing."""


def load() -> Settings:
    return Settings()


def check(settings: Settings) -> None:
    """Raises SettingsError, naming the variable, for one the worker can't work without."""
    for name, value in (
        ("BUCKET_DOCUMENTS", settings.bucket),
        ("QUEUE_DOCUMENT_CONVERSION_URL", settings.conversions_queue_url),
        ("QUEUE_DOCUMENT_UPLOAD_URL", settings.imports_queue_url),
    ):
        if not value:
            raise SettingsError(f"{name} is not set")
