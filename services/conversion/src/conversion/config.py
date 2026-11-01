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


def _float(name: str, default: float) -> float:
    raw = os.environ.get(name)
    if not raw:
        return default
    try:
        return float(raw)
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

    # Keep below the caller's HTTP timeout so this service's error wins the race.
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

    port: int = field(default_factory=lambda: _int("CONVERSION_PORT", 8090))

    # After SIGTERM, how long readiness fails while requests are still taken, so the callers
    # notice. Nothing sets DRAIN_SECONDS: 10 under ProtoStrap (PROTOSTRAP_REPLICA), and a local
    # run stops at once.
    drain_seconds: float = field(
        default_factory=lambda: max(
            0.0, _float("DRAIN_SECONDS", 10.0 if os.environ.get("PROTOSTRAP_REPLICA") else 0.0)
        )
    )


class SettingsError(Exception):
    """A setting the service can't start without is missing."""


def load() -> Settings:
    return Settings()


def check(settings: Settings) -> None:
    """Raises SettingsError, naming the variable, for one the service can't work without."""
    if not settings.bucket:
        raise SettingsError("BUCKET_DOCUMENTS is not set")
