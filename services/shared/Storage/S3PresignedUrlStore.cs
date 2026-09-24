using Amazon.S3;
using Amazon.S3.Model;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Storage;

internal sealed partial class S3ObjectStore
{
    public string PresignPut(string key, string contentType, TimeSpan? ttl = null) =>
        s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(ttl ?? _options.UploadUrlTtl),
        });

    public string PresignGet(string key, TimeSpan? ttl = null) =>
        s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(ttl ?? _options.DownloadUrlTtl),
        });

    /// <summary>
    /// Signs a POST policy whose <c>content-length-range</c> condition makes S3 itself enforce
    /// the size cap.
    ///
    /// <para>Every condition here is exact-match except the range: the browser chooses neither the
    /// key it writes to nor the type it declares, which is what stops a leaked URL from being
    /// aimed at another user's prefix. The credentials are passed rather than taken from the S3
    /// client because a POST policy is signed directly — unlike <c>GetPreSignedURL</c>, which
    /// reaches into the client's own signer.</para>
    /// </summary>
    public PresignedPostPolicy PresignPost(string key, string contentType, long maxBytes, TimeSpan? ttl = null)
    {
        var expiration = DateTimeOffset.UtcNow.Add(ttl ?? _options.UploadUrlTtl);

        // Written by hand rather than serialized: the condition array is order- and shape-
        // sensitive (a two-element range is a different document from a three-element one), and
        // the values in it are either constants or already-validated table entries.
        var policy = $$"""
        {
          "expiration": "{{expiration.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}}",
          "conditions": [
            {"bucket": "{{_options.Bucket}}"},
            {"key": "{{key}}"},
            {"Content-Type": "{{contentType}}"},
            {"success_action_status": "201"},
            ["content-length-range", 1, {{maxBytes}}]
          ]
        }
        """;

        // GetSignedPolicy appends the x-amz-credential / x-amz-algorithm / x-amz-date conditions
        // (and x-amz-security-token for session credentials) to the document before signing it, so
        // adding them above would sign each one twice and every POST would fail the policy.
        var signed = Amazon.S3.Util.S3PostUploadSignedPolicy.GetSignedPolicy(policy, credentials, RegionName);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["key"] = key,
            ["Content-Type"] = contentType,
            ["success_action_status"] = "201",
            ["x-amz-algorithm"] = signed.Algorithm,
            ["x-amz-credential"] = signed.Credential,
            ["x-amz-date"] = signed.Date,
            ["policy"] = signed.Policy,
            ["x-amz-signature"] = signed.Signature,
        };

        if (!string.IsNullOrEmpty(signed.SecurityToken))
        {
            // Instance-role credentials are session credentials, so in production this field is
            // always present and the policy already carries the matching condition.
            fields["x-amz-security-token"] = signed.SecurityToken;
        }

        return new PresignedPostPolicy(BucketEndpoint(), fields, expiration, maxBytes);
    }

    /// <summary>
    /// Where the browser posts the form. LocalStack serves one host for every bucket, so its URL
    /// is path-style; real S3 is addressed virtual-host style, which is also what the
    /// <c>{"bucket": …}</c> policy condition is checked against.
    /// </summary>
    private string BucketEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(s3.Config.ServiceURL))
        {
            return $"{s3.Config.ServiceURL.TrimEnd('/')}/{_options.Bucket}";
        }

        return $"https://{_options.Bucket}.s3.{RegionName}.amazonaws.com";
    }
}
