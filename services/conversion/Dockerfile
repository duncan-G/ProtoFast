# Document → Markdown for ThePlot. CPU only: no CUDA, no torch, no cloud-AI client
# (tests/test_image.py checks requirements.txt, which is exactly what this installs).

# python:3.12-slim (3.12.16 on Debian trixie), pinned by its multi-platform index's digest,
# which has linux/arm64: ProtoStrap builds the image for Graviton.
FROM python:3.12-slim@sha256:aa943ce411423c7582288f18707ff66eddcbaeb510502d747a72aeb34f30d159

# Tesseract for OCR, Ghostscript for OCRmyPDF (installed with pip below), ExifTool for
# MarkItDown's image metadata, poppler-utils for PDFs. Debian builds each for arm64.
RUN apt-get update \
    && apt-get install --no-install-recommends -y \
        tesseract-ocr \
        tesseract-ocr-eng \
        ghostscript \
        libexempi8 \
        libimage-exiftool-perl \
        poppler-utils \
    && rm -rf /var/lib/apt/lists/*

# TMPDIR and HOME: conversions write only under /tmp, the user has no home of its own.
ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PIP_ROOT_USER_ACTION=ignore \
    PYTHONPATH=/app/src \
    TMPDIR=/tmp \
    HOME=/tmp

# opentelemetry-instrument configures traces and metrics from the OTEL_ variables ProtoStrap
# (or Aspire) sets. Logs leave by one path, JSON lines on stdout: OpenTelemetry's logging
# instrumentation would otherwise also send each line over OTLP; it still puts the trace on
# each line for the JSON formatter. Readiness is left out of traces.
ENV OTEL_PYTHON_LOG_AUTO_INSTRUMENTATION=false \
    OTEL_LOGS_EXPORTER=none \
    OTEL_PYTHON_LOG_CORRELATION=true \
    OTEL_PYTHON_FASTAPI_EXCLUDED_URLS=healthz

WORKDIR /app
COPY requirements.txt ./
# Wheels only, exactly the pinned set: the slim image has no compiler, and every pin has an
# arm64 wheel. pip check fails the build if requirements.txt is missing a dependency.
RUN pip install --no-cache-dir --only-binary=:all: --no-deps -r requirements.txt \
    && pip check \
    && tesseract --version \
    && ocrmypdf --version \
    && gs --version
COPY src ./src

# No setuid or setgid binaries, so a process that takes over a parser can't gain privileges
# through one; and not root.
RUN find / -xdev -perm /6000 -type f -exec chmod a-s {} + \
    && useradd --system --no-create-home --uid 1654 conversion
USER 1654

EXPOSE 8090
# One process per replica: ProtoStrap scales replicas, not workers. python -m, not uvicorn's
# command, so the service's own SIGTERM handler drains.
CMD ["opentelemetry-instrument", "python", "-m", "conversion"]
