using Envoy.Service.Auth.V3;
using Google.Protobuf.Collections;
using Google.Rpc;
using Grpc.Core;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Sessions;

namespace ProtoFast.Auth.Api.Services;

/// <summary>
/// Envoy ext_authz <c>Check</c> (guide §3.7). <b>Annotate-only — it never denies.</b> Every request
/// returns OK; a valid session is decorated with trusted identity headers, anything else is passed
/// through as anonymous. Enforcement lives downstream: SSR/SPA redirect for HTML, the backend's
/// internal-JWT check for APIs. Client-supplied identity headers are always stripped or overwritten.
/// </summary>
public sealed class AuthorizationService(
    SessionResolver resolver,
    IOptions<SessionPolicyOptions> sessionOptions,
    ILogger<AuthorizationService> logger) : Authorization.AuthorizationBase
{
    private readonly SessionPolicyOptions _session = sessionOptions.Value;

    public override async Task<CheckResponse> Check(CheckRequest request, ServerCallContext context)
    {
        var headers = request.Attributes?.Request?.Http?.Headers;
        var cookie = GetHeader(headers, "cookie");
        var host = GetHeader(headers, ":authority") ?? GetHeader(headers, "host") ?? GetHeader(headers, "x-forwarded-host");

        ResolvedIdentity? identity = null;
        try
        {
            identity = await resolver.ResolveAsync(cookie, host, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Annotate-only: a resolver failure degrades to anonymous, never a deny.
            logger.LogError(ex, "Session resolution failed; treating request as anonymous");
        }

        var ok = new OkHttpResponse();

        if (identity is not null)
        {
            // Overwrite (append=false) trusted identity onto the request — this also strips any
            // client-supplied copies of these exact headers.
            Set(ok, AuthHeaders.UserId, identity.Subject);
            Set(ok, AuthHeaders.Tenant, identity.Tenant);
            Set(ok, AuthHeaders.Roles, string.Join(',', identity.Roles));
            Set(ok, AuthHeaders.InternalJwt, identity.InternalJwt);
            Set(ok, AuthHeaders.Authenticated, "true");

            if (identity.RotatedSessionId is not null)
            {
                ok.ResponseHeadersToAdd.Add(new HeaderValueOption
                {
                    Header = new HeaderValue { Key = "set-cookie", Value = BuildSessionCookie(identity.RotatedSessionId) },
                    Append = true, // keep alongside any other Set-Cookie the upstream may add
                });
            }
        }
        else
        {
            Set(ok, AuthHeaders.Authenticated, "false");
            // Strip any client-supplied identity headers on anonymous requests (anti-spoofing).
            ok.HeadersToRemove.Add(AuthHeaders.Identity);
        }

        return new CheckResponse
        {
            Status = new Google.Rpc.Status { Code = (int)Code.Ok },
            OkResponse = ok,
        };
    }

    private static void Set(OkHttpResponse ok, string key, string value) =>
        ok.Headers.Add(new HeaderValueOption
        {
            Header = new HeaderValue { Key = key, Value = value },
            Append = false, // overwrite — replaces any inbound value of the same name
        });

    private string BuildSessionCookie(string sessionId) =>
        $"{_session.CookieName}={sessionId}; Path=/; Secure; HttpOnly; SameSite=Lax; Max-Age={(long)_session.AbsoluteTtl.TotalSeconds}";

    private static string? GetHeader(MapField<string, string>? headers, string name)
    {
        if (headers is null)
        {
            return null;
        }

        // Envoy lowercases header keys, but be tolerant of casing just in case.
        if (headers.TryGetValue(name, out var value))
        {
            return value;
        }

        foreach (var pair in headers)
        {
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
