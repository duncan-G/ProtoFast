using System.Security.Cryptography;
using System.Text;

namespace ProtoFast.Auth.Api.Accounts;

/// <summary>
/// An email change that has been asked for and mailed, but not yet proven. It lives in Redis
/// keyed to the account, never in a cookie or a URL: the code is the only thing the user carries,
/// and the address it belongs to has to be the one we mailed, not one the client can restate at
/// confirm time.
/// </summary>
/// <param name="NewEmail">The address the code was mailed to, normalised.</param>
/// <param name="CodeSalt">Per-request salt, so the stored digest of a six-digit code is not a
/// lookup into a table of a million precomputed hashes.</param>
/// <param name="CodeHash">Base64 SHA-256 over salt ‖ code. The code itself is never stored.</param>
/// <param name="Attempts">Wrong codes submitted so far. At <see cref="EmailChangeCode.MaxAttempts"/>
/// the change is dropped and the user starts again — six digits is a small space to guess in.</param>
public sealed record PendingEmailChange(
    string NewEmail,
    string CodeSalt,
    string CodeHash,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    int Attempts = 0);

/// <summary>Generating and checking the six-digit code, and the two numbers that bound it.</summary>
public static class EmailChangeCode
{
    /// <summary>Long enough to find the mail and read it, short enough that a stale code in an
    /// inbox is not a standing key to the account.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>How long before the same mailbox may be mailed again. Cancel does not reset
    /// this: the recipient did not ask for any of this, and giving up on the change is not a
    /// licence to keep writing to them. A different address is a different mailbox.</summary>
    public static readonly TimeSpan RequestCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Mails one account may trigger in <see cref="SendWindow"/>, cancel included.
    /// Stops a session from cycling addresses to get around the per-mailbox wait.</summary>
    public const int MaxSendsPerWindow = 5;

    /// <summary>Window for <see cref="MaxSendsPerWindow"/>.</summary>
    public static readonly TimeSpan SendWindow = TimeSpan.FromMinutes(15);

    /// <summary>Guesses allowed per code. Five of a million, then the change is gone.</summary>
    public const int MaxAttempts = 5;

    /// <summary>A uniformly random six-digit code, leading zeros kept.</summary>
    public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string NewSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    public static string Hash(string salt, string code) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(salt + code)));

    /// <summary>
    /// Whether a submitted code is the one that was mailed. Compared in fixed time — the digest
    /// is not a secret worth timing, but the habit is cheaper to keep than to reason about.
    /// </summary>
    public static bool Matches(PendingEmailChange pending, string code) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(pending.CodeSalt, code)),
            Encoding.UTF8.GetBytes(pending.CodeHash));
}
