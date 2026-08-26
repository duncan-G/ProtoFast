namespace ProtoFast.Auth.Api.Accounts;

/// <summary>
/// Turning what a user typed into an address worth mailing — and worth comparing, which is the
/// less obvious half: the normalised form is matched against the address already on the account
/// and stored as the username, so two spellings of one mailbox must not read as two mailboxes.
/// </summary>
public static class EmailAddress
{
    /// <summary>RFC 5321's ceiling on a path. Anything longer is a paste accident or a probe.</summary>
    public const int MaxLength = 254;

    /// <summary>
    /// Whether <paramref name="raw"/> is an address, and what to store if so. Parsing is
    /// <see cref="System.Net.Mail.MailAddress"/>'s job; the rest is the two rules that make the
    /// result comparable — trimmed, and lowercased.
    /// </summary>
    public static bool TryNormalize(string? raw, out string email)
    {
        email = "";
        var trimmed = (raw ?? "").Trim();
        if (trimmed.Length is 0 or > MaxLength)
        {
            return false;
        }

        // TryCreate also accepts a display name ("Ada <ada@example.com>") and would hand back the
        // address inside it. Only a bare address is an address here, so the round trip has to be
        // exact.
        if (!System.Net.Mail.MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed)
        {
            return false;
        }

        email = trimmed.ToLowerInvariant();
        return true;
    }
}
