"""What the image is allowed to contain. The Dockerfile installs requirements.txt exactly
(--no-deps, then pip check), so checking the file checks the image.

Conversion stays local and CPU-only: no LLM client, no cloud document AI, no GPU runtime,
and none of MarkItDown's extras that send content elsewhere (audio transcription, YouTube)."""

from __future__ import annotations

import re
from pathlib import Path

SERVICE = Path(__file__).resolve().parents[1]
PIN = re.compile(r"^([A-Za-z0-9][A-Za-z0-9._-]*)==([^\s;]+)")

# Listed by name, so adding one is a deliberate, reviewed change.
FORBIDDEN = {
    "openai",
    "anthropic",
    "torch",
    "torchvision",
    "transformers",
    "azure-ai-formrecognizer",
    "azure-ai-documentintelligence",
    "azure-identity",
    "speechrecognition",
    "pydub",
    "youtube-transcript-api",
    "onnxruntime-gpu",
}


def requirements() -> dict[str, str]:
    pins: dict[str, str] = {}
    for raw in (SERVICE / "requirements.txt").read_text().splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        match = PIN.match(line)
        assert match, f"not an exact pin: {line!r}"
        pins[re.sub(r"[-_.]+", "-", match.group(1)).lower()] = match.group(2)
    return pins


def dockerfile() -> str:
    return (SERVICE / "Dockerfile").read_text()


def test_every_package_the_image_installs_is_pinned():
    assert len(requirements()) > 20


def test_no_llm_client_cloud_document_ai_or_gpu_runtime():
    pins = requirements()

    assert not FORBIDDEN & pins.keys()
    assert not [name for name in pins if name.startswith(("nvidia-", "azure-ai-", "cuda"))]


def test_the_direct_dependencies_are_the_locked_versions():
    pyproject = (SERVICE / "pyproject.toml").read_text()
    dependencies = pyproject.split("dependencies = [", 1)[1].split("]\n", 1)[0]
    pins = requirements()

    for name, version in re.findall(r'"([A-Za-z0-9._-]+)(?:\[[^\]]*\])?==([^"]+)"', dependencies):
        assert pins.get(re.sub(r"[-_.]+", "-", name).lower()) == version, name


def test_the_otlp_exporters_for_both_protocols_are_installed():
    # ProtoStrap sets http/protobuf; a local run may say grpc. Either must export.
    pins = requirements()

    assert "opentelemetry-exporter-otlp-proto-http" in pins
    assert "opentelemetry-exporter-otlp-proto-grpc" in pins


def test_the_base_image_is_pinned_by_digest():
    froms = re.findall(r"^FROM\s+(\S+)", dockerfile(), re.MULTILINE)

    assert froms
    assert all(re.search(r"@sha256:[0-9a-f]{64}$", image) for image in froms), froms


def test_it_runs_as_a_user_and_takes_no_build_arguments():
    text = dockerfile()
    users = re.findall(r"^USER\s+(\S+)", text, re.MULTILINE)

    assert users and users[-1] not in {"root", "0"}
    assert not re.search(r"^ARG\s", text, re.MULTILINE)
    # A worker: nothing calls it.
    assert not re.search(r"^EXPOSE\s", text, re.MULTILINE)


def test_logs_leave_by_one_path():
    text = "\n".join(
        line for line in dockerfile().splitlines() if not line.lstrip().startswith("#")
    )

    assert "OTEL_LOGS_EXPORTER=none" in text
    assert "OTEL_PYTHON_LOG_AUTO_INSTRUMENTATION=false" in text
    # The endpoint, protocol and service name are ProtoStrap's (or Aspire's) to set.
    for name in ("OTEL_EXPORTER_OTLP_ENDPOINT", "OTEL_EXPORTER_OTLP_PROTOCOL", "OTEL_SERVICE_NAME"):
        assert name not in text, name
