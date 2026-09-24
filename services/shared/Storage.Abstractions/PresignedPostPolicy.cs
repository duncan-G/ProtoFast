namespace ProtoFast.Storage;

/// <summary>
/// A signed POST policy, ready for a browser to replay as <c>multipart/form-data</c>.
/// </summary>
/// <param name="PostUrl">Where the form is posted.</param>
/// <param name="Fields">
/// Posted verbatim, in this order, <strong>with the file part last</strong> — S3 stops reading at
/// the file part, so a field written after it is never seen and the upload fails the policy.
/// </param>
/// <param name="ExpiresAt">When the policy stops being accepted.</param>
/// <param name="MaxBytes">Echoed so the client's own message can name the same number the policy enforces.</param>
public sealed record PresignedPostPolicy(
    string PostUrl,
    IReadOnlyDictionary<string, string> Fields,
    DateTimeOffset ExpiresAt,
    long MaxBytes);
