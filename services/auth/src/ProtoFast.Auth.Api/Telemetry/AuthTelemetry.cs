using System.Diagnostics;

namespace ProtoFast.Auth.Api.Telemetry;

/// <summary>
/// Tracing source for the auth service's own spans. Registered for export via the
/// <c>ProtoFast.*</c> source filter in ServiceDefaults' <c>ConfigureOpenTelemetry</c>.
/// Used to resume the sign-in trace on the OIDC callback, whose incoming request Keycloak
/// starts a fresh trace for (the callback's auto span is suppressed — see ServiceDefaults).
/// </summary>
internal static class AuthTelemetry
{
    public static readonly ActivitySource Source = new("ProtoFast.Auth");
}
