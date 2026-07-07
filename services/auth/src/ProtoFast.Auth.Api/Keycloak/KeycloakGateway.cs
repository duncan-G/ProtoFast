using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProtoFast.Auth.Api.Configuration;

namespace ProtoFast.Auth.Api.Keycloak;

public sealed class KeycloakGateway(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakOptions> options,
    TimeProvider clock) : IKeycloakGateway
{
    private static readonly TimeSpan KeyCacheTtl = TimeSpan.FromHours(1);

    private readonly KeycloakOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, CachedKeys> _keyCache = new();

    public string BuildAuthorizeUrl(TenantConfig tenant, string redirectUri, string state, string codeChallenge, bool registration)
    {
        var query = Query(
            ("client_id", tenant.ClientId),
            ("response_type", "code"),
            ("scope", "openid profile email"),
            ("redirect_uri", redirectUri),
            ("state", state),
            ("code_challenge", codeChallenge),
            ("code_challenge_method", "S256"),
            ("prompt", registration ? "create" : null));

        return PublicRealmBase(tenant.Realm) + "/protocol/openid-connect/auth" + query;
    }

    public Task<KeycloakTokens> ExchangeCodeAsync(TenantConfig tenant, string code, string redirectUri, string codeVerifier, CancellationToken ct = default) =>
        PostTokenAsync(tenant.Realm, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = tenant.ClientId,
            ["client_secret"] = _options.GetClientSecret(tenant.ClientId),
            ["code_verifier"] = codeVerifier,
        }, ct);

    public Task<KeycloakTokens> RefreshAsync(TenantConfig tenant, string refreshToken, CancellationToken ct = default) =>
        PostTokenAsync(tenant.Realm, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = tenant.ClientId,
            ["client_secret"] = _options.GetClientSecret(tenant.ClientId),
        }, ct);

    public string BuildEndSessionUrl(string realm, string? idTokenHint, string postLogoutRedirectUri) =>
        PublicRealmBase(realm) + "/protocol/openid-connect/logout" + Query(
            ("post_logout_redirect_uri", postLogoutRedirectUri),
            ("id_token_hint", idTokenHint));

    public async Task<TokenValidationParameters> GetValidationParametersAsync(string realm, CancellationToken ct = default)
    {
        var keys = await GetSigningKeysAsync(realm, ct).ConfigureAwait(false);

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = PublicRealmBase(realm),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            // Keycloak puts the client in `azp`, not always `aud`; Check validates it explicitly.
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "preferred_username",
        };
    }

    private async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(string realm, CancellationToken ct)
    {
        if (_keyCache.TryGetValue(realm, out var cached) && cached.ExpiresAt > clock.GetUtcNow())
        {
            return cached.Keys;
        }

        var client = httpClientFactory.CreateClient();
        var json = await client.GetStringAsync(
            InternalRealmBase(realm) + "/protocol/openid-connect/certs", ct).ConfigureAwait(false);

        var keys = new JsonWebKeySet(json).GetSigningKeys().ToArray();
        _keyCache[realm] = new CachedKeys(keys, clock.GetUtcNow() + KeyCacheTtl);
        return keys;
    }

    private async Task<KeycloakTokens> PostTokenAsync(string realm, IDictionary<string, string> form, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(
            InternalRealmBase(realm) + "/protocol/openid-connect/token", content, ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakException($"Keycloak token endpoint returned {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var now = clock.GetUtcNow();

        return new KeycloakTokens(
            AccessToken: root.GetProperty("access_token").GetString()!,
            RefreshToken: root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "",
            IdToken: root.TryGetProperty("id_token", out var idt) ? idt.GetString() : null,
            AccessExpiresAt: now + TimeSpan.FromSeconds(GetInt(root, "expires_in", 300)),
            RefreshExpiresAt: now + TimeSpan.FromSeconds(GetInt(root, "refresh_expires_in", 1800)));
    }

    private static int GetInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var n) ? n : fallback;

    private string InternalRealmBase(string realm) =>
        $"{_options.Authority.TrimEnd('/')}/realms/{Uri.EscapeDataString(realm)}";

    private string PublicRealmBase(string realm) =>
        $"{_options.ResolvePublicAuthority().TrimEnd('/')}/realms/{Uri.EscapeDataString(realm)}";

    private static string Query(params (string Key, string? Value)[] parameters) =>
        "?" + string.Join('&', parameters
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));

    private sealed record CachedKeys(IReadOnlyCollection<SecurityKey> Keys, DateTimeOffset ExpiresAt);
}
