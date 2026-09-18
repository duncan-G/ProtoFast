# The conversion sidecar (ingest plan §10, §20).
#
# Everything here is CPU and Debian-packaged. There is no CUDA runtime, no torch, and no cloud-AI
# client package — CI asserts all three, because that assertion is the only thing that will keep
# the plan's §3 true in a year.

FROM python:3.12-slim-bookworm AS base

# Tesseract and its English data, OCRmyPDF (which brings Ghostscript and pngquant), ExifTool for
# image metadata, and poppler-utils for the PDF helpers. All pinned by the base image tag.
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

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_NO_CACHE_DIR=1 \
    # Conversion is file-based and the container runs read-only with a tmpfs here.
    TMPDIR=/tmp

WORKDIR /app

# Dependencies first, so a source-only change does not re-resolve the whole tree.
COPY pyproject.toml ./
COPY src ./src
RUN pip install --no-compile .

# Unprivileged: the container also drops every capability and forbids privilege escalation, but a
# process that never had root in the first place is the cheaper half of that.
RUN useradd --system --uid 1654 --no-create-home conversion
USER 1654

EXPOSE 8090

# No published port in compose: only the segmentation worker on the same host dials this, by name.
CMD ["conversion"]
