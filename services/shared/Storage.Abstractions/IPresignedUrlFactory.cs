namespace ProtoFast.Storage.Abstractions;

public interface IPresignedUrlFactory
{
    string PresignPut(string key, string contentType, TimeSpan? ttl = null);

    string PresignGet(string key, TimeSpan? ttl = null);

    /// <summary>
    /// Mints a presigned <em>POST</em> for a browser upload.
    ///
    /// </summary>
    /// <param name="key">Pinned exactly by the policy, so one user's URL cannot be aimed at another's prefix.</param>
    /// <param name="contentType">Pinned exactly; the browser must post back what was signed.</param>
    /// <param name="maxBytes">The upper bound S3 itself refuses above, with <c>EntityTooLarge</c>.</param>
    PresignedPostPolicy PresignPost(string key, string contentType, long maxBytes, TimeSpan? ttl = null);
}
