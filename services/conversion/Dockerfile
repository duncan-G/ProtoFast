# CPU only: no CUDA, no torch, no cloud-AI client. deploy-conversion.yml asserts all three.

FROM python:3.12-slim-bookworm

# OCRmyPDF brings Ghostscript and pngquant; ExifTool reads image metadata; poppler-utils for PDFs.
RUN apt-get update \
    && apt-get install --no-install-recommends -y \
        tesseract-ocr \
        tesseract-ocr-eng \
        ocrmypdf \
        ghostscript \
        libexempi8 \
        libimage-exiftool-perl \
        poppler-utils \
    && rm -rf /var/lib/apt/lists/*

# The container runs with a read-only rootfs; /tmp is the only writable path.
ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_NO_CACHE_DIR=1 \
    TMPDIR=/tmp \
    HOME=/tmp \
    OTEL_SERVICE_NAME=conversion \
    OTEL_EXPORTER_OTLP_PROTOCOL=grpc \
    OTEL_PYTHON_LOGGING_AUTO_INSTRUMENTATION_ENABLED=true

WORKDIR /app

COPY pyproject.toml ./
COPY src ./src
RUN pip install --no-compile .

RUN useradd --system --uid 1654 --no-create-home conversion
USER 1654

EXPOSE 8090

CMD ["opentelemetry-instrument", "conversion"]
