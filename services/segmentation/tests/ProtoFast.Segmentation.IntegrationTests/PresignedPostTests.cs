using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// The signed POST policy (ingest plan §7.2).
///
/// <para>This is the one enforcement point in the size story that a client cannot skip, so the
/// policy's <em>contents</em> are asserted rather than just the presence of a signature: S3
/// evaluates exactly these conditions, and a missing <c>content-length-range</c> would leave the
/// limit as something the browser is asked to respect.</para>
/// </summary>
public class PresignedPostTests
{
    private const long TenMebibytes = 10L * 1024 * 1024;

    [Fact]
    public void ThePolicyCarriesTheContentLengthRangeWithTheConfiguredMaximum()
    {
        var conditions = PolicyConditions(Store(), "uploads/alice/upl_1.pdf", "application/pdf", TenMebibytes);

        var range = Assert.Single(conditions.Where(c => c.ValueKind == JsonValueKind.Array));

        Assert.Equal("content-length-range", range[0].GetString());
        // A one-byte floor as well as a ceiling: an empty object is not a document either.
        Assert.Equal(1, range[1].GetInt64());
        Assert.Equal(TenMebibytes, range[2].GetInt64());
    }

    [Fact]
    public void TheKeyAndTheContentTypeArePinnedExactly()
    {
        const string key = "uploads/alice/upl_1.pdf";
        var conditions = PolicyConditions(Store(), key, "application/pdf", TenMebibytes);

        Assert.Equal(key, Exact(conditions, "key"));
        Assert.Equal("application/pdf", Exact(conditions, "Content-Type"));
        Assert.Equal("test-bucket", Exact(conditions, "bucket"));
    }

    [Fact]
    public void TheSigningConditionsAreNotDuplicated()
    {
        // GetSignedPolicy appends x-amz-credential / x-amz-algorithm / x-amz-date itself. Adding
        // them by hand would sign each twice and every POST would fail with "Policy Condition
        // failed", so this asserts exactly one of each survives.
        var conditions = PolicyConditions(Store(), "uploads/alice/upl_1.pdf", "application/pdf", TenMebibytes);

        foreach (var name in (string[])["x-amz-credential", "x-amz-algorithm", "x-amz-date"])
        {
            Assert.Single(conditions.Where(c => c.ValueKind == JsonValueKind.Object && c.TryGetProperty(name, out _)));
        }
    }

    [Fact]
    public void TheFormCarriesEverythingTheBrowserHasToPostBack()
    {
        var post = Store().PresignPost("uploads/alice/upl_1.pdf", "application/pdf", TenMebibytes);

        foreach (var field in (string[])
            ["key", "Content-Type", "success_action_status", "policy", "x-amz-signature",
             "x-amz-algorithm", "x-amz-credential", "x-amz-date"])
        {
            Assert.True(post.Fields.ContainsKey(field), $"the form is missing '{field}'");
        }

        // Echoed so the client's "that file is larger than…" message names the same number the
        // policy enforces, rather than a second copy of the limit that can drift from it.
        Assert.Equal(TenMebibytes, post.MaxBytes);
    }

    [Fact]
    public void TheEndpointIsPathStyleAgainstALocalStackServiceUrl() =>
        Assert.Equal(
            "http://localhost:4566/test-bucket",
            Store().PresignPost("uploads/alice/upl_1.pdf", "application/pdf", TenMebibytes).PostUrl);

    /// <summary>A store pointed at a LocalStack-shaped endpoint; no call is made, only a signature.</summary>
    private static S3ArtifactStore Store() =>
        new(
            new AmazonS3Client(
                new BasicAWSCredentials("localstack", "localstack"),
                new AmazonS3Config
                {
                    ServiceURL = "http://localhost:4566",
                    ForcePathStyle = true,
                    AuthenticationRegion = "us-west-2",
                }),
            new BasicAWSCredentials("localstack", "localstack"),
            Options.Create(new StorageOptions { Bucket = "test-bucket", Region = "us-west-2" }),
            NullLogger<S3ArtifactStore>.Instance);

    private static List<JsonElement> PolicyConditions(
        S3ArtifactStore store, string key, string contentType, long maxBytes)
    {
        var post = store.PresignPost(key, contentType, maxBytes);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(post.Fields["policy"]));

        return [.. JsonDocument.Parse(json).RootElement.GetProperty("conditions").EnumerateArray()];
    }

    private static string? Exact(IEnumerable<JsonElement> conditions, string name) => conditions
        .Where(c => c.ValueKind == JsonValueKind.Object && c.TryGetProperty(name, out _))
        .Select(c => c.GetProperty(name).GetString())
        .FirstOrDefault();
}
