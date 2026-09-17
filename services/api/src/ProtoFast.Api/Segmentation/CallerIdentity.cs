using System.Security.Claims;
using Grpc.Core;
using ProtoFast.ServiceDefaults.InternalAuth;

namespace ProtoFast.Api;

/// <summary>
/// Who is calling, taken from the internal JWT and from nowhere else.
///
/// <para>This is the single enforcement point for F17. Every query in the service filters on
/// <see cref="Subject"/>, and <see cref="Subject"/> can only come from a validated token — there
/// is deliberately no way to pass an owner in a request, because a request field would be a
/// client-controlled authorization key.</para>
/// </summary>
public sealed record CallerIdentity(string Subject, IReadOnlySet<string> Roles)
{
    public static CallerIdentity From(ServerCallContext context)
    {
        var principal = InternalJwtAuthInterceptor.GetPrincipal(context)
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "No internal principal on this call."));

        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "The internal token carries no subject."));
        }

        var roles = principal.FindAll("roles")
            .SelectMany(c => c.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new CallerIdentity(subject, roles);
    }

    public bool HasRole(string role) => Roles.Contains(role);

    public void RequireRole(string role)
    {
        if (!HasRole(role))
        {
            // Deliberately does not say which role is missing: a caller without it has no need to
            // know the role's name, and naming it is free reconnaissance.
            throw new RpcException(new Status(StatusCode.PermissionDenied, "This operation is not available to you."));
        }
    }
}
