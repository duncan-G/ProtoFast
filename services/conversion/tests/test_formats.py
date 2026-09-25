"""Which handler a request reaches, and which requests reach none."""

from __future__ import annotations

import pytest

from conversion.formats import FORMATS, extension_of, resolve


@pytest.mark.parametrize(
    ("key", "media_type", "handler"),
    [
        ("uploads/a/upl_1.md", "text/markdown", "passthrough"),
        ("uploads/a/upl_1.txt", "text/plain", "passthrough"),
        ("uploads/a/upl_1.pdf", "application/pdf", "pdf"),
        ("uploads/a/upl_1.png", "image/png", "image"),
        ("uploads/a/upl_1.tiff", "image/tiff", "image"),
        ("uploads/a/upl_1.epub", "application/epub+zip", "markitdown"),
        ("uploads/a/upl_1.csv", "text/csv", "markitdown"),
    ],
)
def test_an_allowed_pair_reaches_the_expected_handler(key, media_type, handler):
    fmt = resolve(key, media_type)
    assert fmt is not None
    assert fmt.handler == handler


@pytest.mark.parametrize(
    ("key", "media_type"),
    [
        ("uploads/a/upl_1.zip", "application/zip"),
        ("uploads/a/upl_1.mp3", "audio/mpeg"),
        ("uploads/a/upl_1.doc", "application/msword"),
        ("uploads/a/upl_1", "application/pdf"),
        ("uploads/a/upl_1.pdf", "audio/mpeg"),
        ("uploads/a/upl_1.xlsx", "application/pdf"),
    ],
)
def test_a_pair_that_api_would_not_have_signed_is_refused(key, media_type):
    assert resolve(key, media_type) is None


def test_a_media_type_with_parameters_still_matches():
    assert resolve("uploads/a/upl_1.txt", "text/plain; charset=utf-8") is not None


def test_an_absent_media_type_falls_back_to_the_extension():
    fmt = resolve("uploads/a/upl_1.pdf", "")
    assert fmt is not None and fmt.handler == "pdf"


def test_resolution_is_case_insensitive():
    assert resolve("uploads/a/upl_1.PDF", "APPLICATION/PDF") is not None


@pytest.mark.parametrize(
    ("key", "expected"),
    [
        ("uploads/a/upl_1.pdf", ".pdf"),
        ("uploads/a/UPL_1.PDF", ".pdf"),
        ("upl_1.tar.gz", ".gz"),
        ("uploads/a/upl_1", None),
        ("uploads/a/upl_1.", None),
        ("uploads/a.b/upl_1", None),
    ],
)
def test_the_extension_comes_from_the_last_path_segment(key, expected):
    assert extension_of(key) == expected


def test_only_markdown_and_text_bypass_conversion():
    passthrough = sorted(f.extension for f in FORMATS if not f.requires_conversion)
    assert passthrough == [".markdown", ".md", ".txt"]
