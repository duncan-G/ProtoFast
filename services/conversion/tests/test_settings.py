"""What the service reads from its environment under ProtoStrap and `aspire run`: the bucket
from BUCKET_DOCUMENTS, the region from AWS_REGION, and LocalStack from AWS_ENDPOINT_URL_S3,
which boto3 reads itself."""

from __future__ import annotations

import pytest

from conversion import config
from conversion.storage import Storage

AWS_VARIABLES = (
    "AWS_ENDPOINT_URL",
    "AWS_ENDPOINT_URL_S3",
    "AWS_REGION",
    "AWS_DEFAULT_REGION",
    "AWS_PROFILE",
    "AWS_CONFIG_FILE",
    "AWS_SHARED_CREDENTIALS_FILE",
    "AWS_IGNORE_CONFIGURED_ENDPOINT_URLS",
)


@pytest.fixture
def aws(monkeypatch, tmp_path):
    """No developer's AWS configuration leaks in; test credentials so URLs can be signed."""
    for name in AWS_VARIABLES:
        monkeypatch.delenv(name, raising=False)
    monkeypatch.setenv("AWS_CONFIG_FILE", str(tmp_path / "config"))
    monkeypatch.setenv("AWS_SHARED_CREDENTIALS_FILE", str(tmp_path / "credentials"))
    monkeypatch.setenv("AWS_ACCESS_KEY_ID", "test")
    monkeypatch.setenv("AWS_SECRET_ACCESS_KEY", "test")
    return monkeypatch


def object_url(storage: Storage, key: str = "uploads/a/upl_1.pdf") -> str:
    return storage._s3.generate_presigned_url(
        "get_object", Params={"Bucket": storage.bucket, "Key": key}
    ).split("?")[0]


def test_the_bucket_is_BUCKET_DOCUMENTS_alone(monkeypatch):  # noqa: N802
    monkeypatch.setenv("BUCKET_DOCUMENTS", "protofast-dev-documents")

    settings = config.load()

    assert settings.bucket == "protofast-dev-documents"
    assert Storage(settings, client=object()).bucket == "protofast-dev-documents"


def test_the_old_conversion_variables_are_not_read(monkeypatch):
    monkeypatch.delenv("BUCKET_DOCUMENTS", raising=False)
    monkeypatch.setenv("CONVERSION_BUCKET", "old")
    monkeypatch.setenv("CONVERSION_S3_ENDPOINT", "http://old:4566")

    settings = config.load()

    assert settings.bucket == ""
    assert settings.s3_endpoint is None


def test_without_BUCKET_DOCUMENTS_it_refuses_to_start_and_says_why(monkeypatch):  # noqa: N802
    monkeypatch.delenv("BUCKET_DOCUMENTS", raising=False)

    with pytest.raises(config.SettingsError, match="BUCKET_DOCUMENTS"):
        config.check(config.load())


def test_with_BUCKET_DOCUMENTS_it_starts(monkeypatch):  # noqa: N802
    monkeypatch.setenv("BUCKET_DOCUMENTS", "x")

    config.check(config.load())


def test_the_region_is_AWS_REGION_which_botocore_does_not_read_itself(aws):  # noqa: N802
    aws.setenv("AWS_REGION", "eu-west-1")
    aws.setenv("BUCKET_DOCUMENTS", "docs")

    storage = Storage(config.load())

    assert storage._s3.meta.region_name == "eu-west-1"


def test_in_an_account_it_talks_to_s3_in_its_region(aws):
    aws.setenv("AWS_REGION", "eu-west-1")
    aws.setenv("BUCKET_DOCUMENTS", "docs")

    storage = Storage(config.load())

    assert storage._s3.meta.endpoint_url == "https://s3.eu-west-1.amazonaws.com"
    assert object_url(storage).startswith("https://docs.s3.")


@pytest.mark.parametrize("variable", ["AWS_ENDPOINT_URL_S3", "AWS_ENDPOINT_URL"])
def test_under_aspire_it_talks_to_localstack_path_style(aws, variable):
    # LocalStack serves every bucket from one host, so docs.localstack wouldn't resolve.
    aws.setenv("AWS_REGION", "us-east-1")
    aws.setenv("BUCKET_DOCUMENTS", "docs")
    aws.setenv(variable, "http://localstack:4566")

    storage = Storage(config.load())

    assert storage._s3.meta.endpoint_url == "http://localstack:4566"
    assert object_url(storage) == "http://localstack:4566/docs/uploads/a/upl_1.pdf"


class RecordingClient:
    def __init__(self) -> None:
        self.calls: list[tuple[str, dict]] = []

    def put_object(self, **kwargs):
        self.calls.append(("put_object", kwargs))
        return {}

    def head_bucket(self, **kwargs):
        self.calls.append(("head_bucket", kwargs))
        return {}


def test_every_write_carries_a_checksum_which_an_object_lock_bucket_requires():
    client = RecordingClient()
    storage = Storage(config.Settings(bucket="docs"), client=client)

    storage.put_text("uploads/a/upl_1.md", "# hi", "text/markdown", "sha256:x", "upl_1")
    storage.put_json("uploads/a/upl_1.layout.json", {"lines": []}, "sha256:x", "upl_1")

    assert [kwargs["ChecksumAlgorithm"] for _, kwargs in client.calls] == ["CRC32", "CRC32"]
    assert all(kwargs["Bucket"] == "docs" for _, kwargs in client.calls)


def test_the_bucket_answers_when_s3_heads_it():
    client = RecordingClient()
    storage = Storage(config.Settings(bucket="docs"), client=client)

    assert storage.bucket_answers()
    assert client.calls == [("head_bucket", {"Bucket": "docs"})]


def test_the_bucket_does_not_answer_when_s3_refuses(aws):
    from botocore.exceptions import ClientError  # noqa: PLC0415

    class Refusing:
        def head_bucket(self, **_):
            raise ClientError({"Error": {"Code": "403", "Message": "Forbidden"}}, "HeadBucket")

    assert not Storage(config.Settings(bucket="docs"), client=Refusing()).bucket_answers()


def test_the_bucket_does_not_answer_when_s3_is_unreachable(aws):
    from botocore.exceptions import EndpointConnectionError  # noqa: PLC0415

    class Unreachable:
        def head_bucket(self, **_):
            raise EndpointConnectionError(endpoint_url="http://localstack:4566")

    assert not Storage(config.Settings(bucket="docs"), client=Unreachable()).bucket_answers()
