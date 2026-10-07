using Grpc.Core;

namespace ProtoFast.Grpc;

/// <summary>
/// The check every admin RPC opens with (docs/design/admin-consoles.md §4.3). The tenant check
/// stops a role of the same name in an app's own realm from counting.
/// </summary>
public static class AdminAccess
{
    public const string OperatorsTenant = "operators";

    public const string PlatformRole = "platform";

    /// <summary>An app id is the name of the app's own realm.</summary>
    public static IReadOnlySet<string> KnownApps { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "protofast",
        "theplot",
    };

    public static string RoleFor(string app) => $"admin-{app}";

    /// <summary>For RPCs whose request names the app.</summary>
    public static CallerIdentity Require(ServerCallContext context, string app)
    {
        var caller = CallerIdentity.From(context);
        if (!KnownApps.Contains(app) || !IsOperatorWith(caller, RoleFor(app)))
        {
            throw Denied();
        }

        return caller;
    }

    /// <summary>For RPCs keyed by id: another app's row answers NotFound, like a missing one.</summary>
    public static CallerIdentity RequireRow(ServerCallContext context, string rowApp, string notFoundMessage)
    {
        var caller = CallerIdentity.From(context);
        if (!KnownApps.Contains(rowApp) || !IsOperatorWith(caller, RoleFor(rowApp)))
        {
            throw new RpcException(new Status(StatusCode.NotFound, notFoundMessage));
        }

        return caller;
    }

    public static CallerIdentity RequirePlatform(ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);
        if (!IsOperatorWith(caller, PlatformRole))
        {
            throw Denied();
        }

        return caller;
    }

    private static bool IsOperatorWith(CallerIdentity caller, string role) =>
        string.Equals(caller.Tenant, OperatorsTenant, StringComparison.Ordinal) && caller.HasRole(role);

    private static RpcException Denied() =>
        new(new Status(StatusCode.PermissionDenied, "This operation is not available to you."));
}
