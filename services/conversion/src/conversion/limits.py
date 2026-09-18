"""Size, page, time and subprocess guards (ingest plan §10).

Every one of these exists because the converter is the first thing in the platform that loads a
user's bytes into memory and shells out to a C library over them. The container's own limits —
1 GB memory, read-only rootfs, tmpfs-only writes — are the backstop; these are the ones that
produce an error a person can act on instead of an OOM kill.
"""

from __future__ import annotations

import os
import signal
import subprocess
import time
from dataclasses import dataclass


class ConversionError(Exception):
    """A failure with the status and code the contract promises for it (ingest plan §9)."""

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
    """The wall clock for one conversion.

    Checked between steps rather than enforced by a signal: a half-written S3 object is worse
    than a conversion that runs ten seconds past its budget, and every step here is bounded
    anyway by its own subprocess timeout.
    """

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
    """The backstop of §7.3, check 4.

    The POST policy already refused anything bigger at the edge of the platform. This catches the
    two cases the policy cannot: an object written before the limit was lowered, and a key that
    was somehow populated by something other than a policy this service signed.
    """
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
            "This document converts to more text than ThePlot can segment. "
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
    """Runs a pinned third-party binary with a wall-clock timeout (ingest plan N4).

    The child gets its own process group and the *group* is killed on timeout: OCRmyPDF forks
    Ghostscript and Tesseract, and killing only the parent would leave those running against a
    tmpfs the request is about to release.
    """
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
    """Best effort: the child is already being torn down by subprocess.run's own handling."""
    pid = getattr(exc, "pid", None)
    if not pid:
        return
    try:
        os.killpg(os.getpgid(pid), signal.SIGKILL)
    except (ProcessLookupError, PermissionError):
        pass
