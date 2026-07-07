using System.Security.Cryptography;

namespace ProtoFast.Auth.Api.Sessions;

public static class SessionIds
{
    /// <summary>32 random bytes, base64url (no padding) — an unguessable opaque session id.</summary>
    public static string Generate(int numBytes = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(numBytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>Extracts a single cookie value from a raw <c>Cookie</c> header, case-insensitively.</summary>
    public static string? ParseCookie(string? cookieHeader, string cookieName)
    {
        if (string.IsNullOrEmpty(cookieHeader))
        {
            return null;
        }

        foreach (var part in cookieHeader.Split(';'))
        {
            var span = part.AsSpan().Trim();
            var eq = span.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (span[..eq].Trim().Equals(cookieName, StringComparison.OrdinalIgnoreCase))
            {
                return span[(eq + 1)..].Trim().ToString();
            }
        }

        return null;
    }
}
