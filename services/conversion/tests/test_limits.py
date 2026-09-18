"""The guards (ingest plan §10, §23).

Each of these is the difference between an error a person can act on and an OOM kill, so what is
asserted is the *status code* as much as the refusal: the caller uses 4xx versus 5xx to decide
between failing the run now and letting SQS redeliver.
"""

from __future__ import annotations

import pytest

from conversion import limits

TEN_MIB = 10 * 1024 * 1024


def test_a_source_over_the_cap_is_a_permanent_413():
    with pytest.raises(limits.ConversionError) as caught:
        limits.check_source_size(TEN_MIB + 1, TEN_MIB)

    assert caught.value.status == 413
    assert caught.value.code == "source_too_large"


def test_a_source_at_the_cap_is_accepted():
    limits.check_source_size(TEN_MIB, TEN_MIB)


def test_an_empty_source_is_refused():
    with pytest.raises(limits.ConversionError) as caught:
        limits.check_source_size(0, TEN_MIB)

    assert caught.value.status == 422


def test_output_over_the_cap_names_what_the_user_can_do():
    with pytest.raises(limits.ConversionError) as caught:
        limits.check_output_size(TEN_MIB, 1024)

    # A spreadsheet of a million rows is the case; "split it into sections" is the action.
    assert caught.value.status == 422
    assert "split" in caught.value.message.lower()


def test_too_many_pages_is_refused_before_any_of_them_are_read():
    with pytest.raises(limits.ConversionError) as caught:
        limits.check_page_count(2001, 2000)

    assert caught.value.status == 422
    assert "2001" in caught.value.message


def test_an_exhausted_budget_is_a_retryable_504():
    budget = limits.Budget(seconds=0.0, started=limits.time.monotonic())

    with pytest.raises(limits.ConversionError) as caught:
        budget.check("OCR")

    # 504 rather than 4xx: the caller retries once, and only a second timeout fails the run.
    assert caught.value.status == 504
    assert caught.value.code == "conversion_timeout"


def test_a_budget_with_time_left_does_not_trip():
    limits.Budget.start(60).check("OCR")


def test_a_missing_binary_is_reported_as_a_converter_fault_not_a_document_one():
    with pytest.raises(limits.ConversionError) as caught:
        limits.run(["/nonexistent/ocrmypdf"], timeout=5, step="OCR")

    # 500, so the run is retried: the document is fine, the image is not.
    assert caught.value.status == 500


def test_a_failing_binary_carries_its_stderr_as_detail():
    with pytest.raises(limits.ConversionError) as caught:
        limits.run(["sh", "-c", "echo boom >&2; exit 3"], timeout=5, step="OCR")

    assert caught.value.status == 500
    assert "boom" in (caught.value.detail or "")


def test_a_hanging_binary_is_killed_and_reported_as_a_timeout():
    with pytest.raises(limits.ConversionError) as caught:
        limits.run(["sh", "-c", "sleep 30"], timeout=1, step="OCR")

    assert caught.value.status == 504
