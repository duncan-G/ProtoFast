"""Size, page, time and subprocess guards. The container's memory cap and read-only rootfs are the
backstop; these turn the common cases into an error a person can act on."""

from __future__ import annotations

import os
import signal
import subprocess
import time
from dataclasses import dataclass


class ConversionError(Exception):
    def __init__(self, status: int, code: str, message: str, detail: str | None = None) -> None:
        super().__init__(message)
        self.status = status
        self.code = code
        self.message = message
        self.detail = detail


def unsupported(message: str) -> ConversionError:
    return ConversionError(422, "unsupported_format", message)


def too_large(message: str) -> ConversionError:
    return ConversionError(413, "source_too_large", message)


def timed_out(message: str) -> ConversionError:
    return ConversionError(504, "conversion_timeout", message)


def failed(message: str, detail: str | None = None) -> ConversionError:
    return ConversionError(500, "conversion_failed", message, detail)


@dataclass
class Budget:
    """Wall clock for one conversion, checked between steps rather than by a signal so an S3
    write is never cut in half."""

    seconds: float
    started: float

    @classmethod
    def start(cls, seconds: float) -> Budget:
        return cls(seconds=seconds, started=time.monotonic())

    @property
    def elapsed(self) -> float:
        return time.monotonic() - self.started

    @property
    def remaining(self) -> float:
        return max(0.0, self.seconds - self.elapsed)

    def check(self, step: str) -> None:
        if self.remaining <= 0:
            raise timed_out(f"This document takes too long to convert (stopped during {step}).")


def check_source_size(size: int, maximum: int) -> None:
    if size <= 0:
        raise unsupported("The uploaded document is empty.")
    if size > maximum:
        megabytes = size // (1024 * 1024)
        limit = maximum // (1024 * 1024)
        raise too_large(f"That file is {megabytes} MB, over the {limit} MB limit.")


def check_output_size(size: int, maximum: int) -> None:
    if size > maximum:
        raise ConversionError(
            422,
            "output_too_large",
            "This document converts to more text than ThePlot can handle. "
            "Try splitting it into sections.",
        )


def check_page_count(pages: int, maximum: int) -> None:
    if pages > maximum:
        raise ConversionError(
            422,
            "too_many_pages",
            f"This document has {pages} pages, over the {maximum}-page limit.",
        )


def run(argv: list[str], timeout: float, step: str) -> subprocess.CompletedProcess[bytes]:
    """Runs a binary in its own process group, so a timeout also kills the Ghostscript and
    Tesseract children OCRmyPDF forks."""
    try:
        return subprocess.run(  # noqa: S603 - argv is built here, never from request data
            argv,
            capture_output=True,
            timeout=max(1.0, timeout),
            check=True,
            start_new_session=True,
        )
    except subprocess.TimeoutExpired as exc:
        _kill_group(exc)
        raise timed_out(
            f"This document takes too long to convert (stopped during {step})."
        ) from exc
    except subprocess.CalledProcessError as exc:
        stderr = (exc.stderr or b"").decode("utf-8", "replace").strip()
        raise failed(
            f"The document could not be converted ({step} failed).", stderr[:2000]
        ) from exc
    except FileNotFoundError as exc:
        raise failed(f"The converter image is missing {argv[0]}.") from exc


def _kill_group(exc: subprocess.TimeoutExpired) -> None:
    pid = getattr(exc, "pid", None)
    if not pid:
        return
    try:
        os.killpg(os.getpgid(pid), signal.SIGKILL)
    except (ProcessLookupError, PermissionError):
        pass
