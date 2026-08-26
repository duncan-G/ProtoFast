using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;

namespace ProtoFast.Auth.Api.Keycloak;

/// <summary>
/// Admin API client for the account-management endpoints. Talks to the INTERNAL authority only —
/// nothing here is browser-facing — and authenticates with the client-credentials grant of the
/// per-realm admin service account (<see cref="KeycloakOptions.AdminClientId"/>).
/// </summary>
public sealed class KeycloakAdmin(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakOptions> options,
    TimeProvider clock,
    ILogger<KeycloakAdmin> logger) : IKeycloakAdmin
{
    /// <summary>Re-fetch a little before the token lapses so a call never rides an expired one.</summary>
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private const string PasswordlessType = "webauthn-passwordless";
    private const string TwoFactorType = "webauthn";

    private readonly KeycloakOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<WebAuthnCredential>> ListWebAuthnCredentialsAsync(
        string realm, string subject, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserBase(realm, subject) + "/credentials");
        using var response = await SendAsync(realm, request, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The Keycloak user is gone but the session outlived it. Nothing to list, and the
            // caller's own error handling is a better place to decide what that means.
            return [];
        }

        await ThrowIfFailedAsync(response, "list credentials", ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return doc.RootElement.EnumerateArray()
            .Select(ReadCredential)
            .Where(c => c is not null)
            .Select(c => c!)
            .OrderByDescending(c => c.CreatedAt ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    public async Task<bool> DeleteCredentialAsync(
        string realm, string subject, string credentialId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{UserBase(realm, subject)}/credentials/{Uri.EscapeDataString(credentialId)}");
        using var response = await SendAsync(realm, request, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await ThrowIfFailedAsync(response, "delete credential", ct).ConfigureAwait(false);
        return true;
    }

    public async Task DeleteUserAsync(string realm, string subject, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, UserBase(realm, subject));
        using var response = await SendAsync(realm, request, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation(
                "Keycloak user {Subject} in realm {Realm} was already gone; treating the delete as done",
                subject, realm);
            return;
        }

        await ThrowIfFailedAsync(response, "delete user", ct).ConfigureAwait(false);
    }

    public async Task<bool> IsEmailTakenAsync(
        string realm, string email, string exceptSubject, CancellationToken ct = default)
    {
        // Both fields, because the write conflicts on either. The realm has
        // registrationEmailAsUsername, so for anything it registered the two hold the same string
        // and the second query is a formality — but a user an operator created by hand can have a
        // username that is somebody else's future email address, and that still 409s at the PUT.
        return await AnyOtherUserAsync(realm, "email", email, exceptSubject, ct).ConfigureAwait(false)
               || await AnyOtherUserAsync(realm, "username", email, exceptSubject, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the realm holds a user matching <paramref name="field"/> exactly, other than
    /// <paramref name="exceptSubject"/>. <c>exact</c> keeps a substring of a longer address from
    /// reading as a conflict; two records are enough to answer, since the only match that is not
    /// an answer is the caller's own.
    /// </summary>
    private async Task<bool> AnyOtherUserAsync(
        string realm, string field, string value, string exceptSubject, CancellationToken ct)
    {
        var query = $"?{field}={Uri.EscapeDataString(value)}&exact=true&briefRepresentation=true&max=2";
        using var request = new HttpRequestMessage(HttpMethod.Get, UsersBase(realm) + query);
        using var response = await SendAsync(realm, request, ct).ConfigureAwait(false);

        await ThrowIfFailedAsync(response, $"search users by {field}", ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return doc.RootElement.EnumerateArray().Any(user =>
            user.TryGetProperty("id", out var id)
            && id.GetString() is { Length: > 0 } found
            && !string.Equals(found, exceptSubject, StringComparison.Ordinal));
    }

    public async Task<EmailUpdateOutcome> UpdateEmailAsync(
        string realm, string subject, string email, CancellationToken ct = default)
    {
        // Read-modify-write rather than a bare PUT of the three fields we care about. Keycloak's
        // user endpoint takes a whole representation, and anything omitted from it — first name,
        // required actions, custom attributes — is a field the update would quietly blank.
        using var read = new HttpRequestMessage(HttpMethod.Get, UserBase(realm, subject));
        using var current = await SendAsync(realm, read, ct).ConfigureAwait(false);

        if (current.StatusCode == HttpStatusCode.NotFound)
        {
            return EmailUpdateOutcome.UserGone;
        }

        await ThrowIfFailedAsync(current, "read the user", ct).ConfigureAwait(false);

        var user = JsonNode.Parse(await current.Content.ReadAsStringAsync(ct).ConfigureAwait(false))?.AsObject()
                   ?? throw new KeycloakException("Keycloak returned a user representation that is not an object.");

        user["email"] = email;
        // registrationEmailAsUsername: the address is the username. Written explicitly rather
        // than left to the realm to derive, so the two can never end up describing different
        // mailboxes.
        user["username"] = email;
        // We mailed a code to this address and the user read it back to us, which is the same
        // proof Keycloak's own verification mail collects.
        user["emailVerified"] = true;

        using var write = new HttpRequestMessage(HttpMethod.Put, UserBase(realm, subject))
        {
            Content = new StringContent(user.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await SendAsync(realm, write, ct).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => EmailUpdateOutcome.UserGone,
            // duplicateEmailsAllowed is off, so an address another account already holds comes
            // back as a conflict. It is the user's answer, not an outage.
            HttpStatusCode.Conflict => EmailUpdateOutcome.AddressTaken,
            _ => await UpdatedOrThrowAsync(response, ct).ConfigureAwait(false),
        };
    }

    private static async Task<EmailUpdateOutcome> UpdatedOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await ThrowIfFailedAsync(response, "update the email address", ct).ConfigureAwait(false);
        return EmailUpdateOutcome.Updated;
    }

    /// <summary>
    /// Sends an admin request with a cached service-account token, retrying once on a 401 with a
    /// freshly minted one. A token can die before its stated expiry — a realm restart, a rotated
    /// secret — and the retry is what keeps that from surfacing as a user-visible failure.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(string realm, HttpRequestMessage request, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await GetTokenAsync(realm, forceRefresh: false, ct).ConfigureAwait(false));
        var response = await client.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        using var retry = await CloneAsync(request, ct).ConfigureAwait(false);
        retry.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await GetTokenAsync(realm, forceRefresh: true, ct).ConfigureAwait(false));
        return await client.SendAsync(retry, ct).ConfigureAwait(false);
    }

    private async Task<string> GetTokenAsync(string realm, bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh
            && _tokens.TryGetValue(realm, out var cached)
            && cached.ExpiresAt > clock.GetUtcNow() + ExpirySkew)
        {
            return cached.AccessToken;
        }

        if (string.IsNullOrEmpty(_options.AdminClientSecret))
        {
            throw new KeycloakException(
                $"No admin client secret configured for realm '{realm}'; account management is unavailable.");
        }

        var client = httpClientFactory.CreateClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.AdminClientId,
            ["client_secret"] = _options.AdminClientSecret,
        });

        using var response = await client.PostAsync(
            RealmBase(realm) + "/protocol/openid-connect/token", content, ct).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, "admin token", ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var token = root.GetProperty("access_token").GetString()!;
        var lifetime = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(1);

        _tokens[realm] = new CachedToken(token, clock.GetUtcNow() + lifetime);
        return token;
    }

    private static WebAuthnCredential? ReadCredential(JsonElement element)
    {
        var type = element.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type is not (PasswordlessType or TwoFactorType))
        {
            return null;
        }

        var id = element.TryGetProperty("id", out var i) ? i.GetString() : null;
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var label = element.TryGetProperty("userLabel", out var l) ? l.GetString() ?? "" : "";

        // createdDate is epoch milliseconds, and is absent on credentials old enough to predate it.
        DateTimeOffset? createdAt = element.TryGetProperty("createdDate", out var c) && c.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

        return new WebAuthnCredential(id, label, createdAt, type == PasswordlessType);
    }

    /// <summary>
    /// A fresh copy for the 401 retry. The body is re-created from the string the caller built
    /// rather than the spent <see cref="HttpContent"/>, whose stream the first attempt consumed.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            clone.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return clone;
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new KeycloakException(
            $"Keycloak admin API refused to {what}: {(int)response.StatusCode}{ErrorSuffix(body)}.",
            response.StatusCode);
    }

    /// <summary>Keycloak's own reason for refusing, when it gave one — the admin API answers with
    /// <c>error</c> or <c>errorMessage</c> depending on the endpoint.</summary>
    private static string ErrorSuffix(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var name in (string[])["error_description", "errorMessage", "error"])
            {
                if (doc.RootElement.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text)
                {
                    return $" ({text})";
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (an HTML error page from something in front of Keycloak) — the status alone.
        }

        return "";
    }

    private string RealmBase(string realm) =>
        $"{_options.Authority.TrimEnd('/')}/realms/{Uri.EscapeDataString(realm)}";

    private string UsersBase(string realm) =>
        $"{_options.Authority.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(realm)}/users";

    private string UserBase(string realm, string subject) =>
        $"{UsersBase(realm)}/{Uri.EscapeDataString(subject)}";

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);
}
