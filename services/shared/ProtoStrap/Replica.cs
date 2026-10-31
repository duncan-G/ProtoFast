using System.Collections;

namespace ProtoFast.ProtoStrap;

/// <summary>
/// Replica is what ProtoStrap tells every replica (its application contract), and what the
/// AppHost sets the same way under <c>aspire run</c>.
/// </summary>
public static class Replica
{
    /// <summary>UnderProtoStrap says whether this is a replica in an account, not a local run.</summary>
    public static bool UnderProtoStrap => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROTOSTRAP_REPLICA"));

    /// <summary>Component is the replica's component, which its readiness answers for, or null locally.</summary>
    public static string? Component => Variable("PROTOSTRAP_COMPONENT");

    /// <summary>
    /// Hostname is a host's hostname in this account, such as admin.protofast.dev for admin,
    /// from HOST_&lt;NAME&gt;, or null when the host isn't one of the spec's.
    /// </summary>
    public static string? Hostname(string host) => Variable(Name("HOST_", host));

    /// <summary>Origin is a host's origin, such as https://admin.protofast.dev, from ORIGIN_&lt;NAME&gt;.</summary>
    public static string? Origin(string host) => Variable(Name("ORIGIN_", host));

    /// <summary>
    /// OriginFor is the origin browsers use for a request to <paramref name="host"/>, a Host
    /// header with or without its port: ORIGIN_&lt;NAME&gt; of the spec's host whose HOST_&lt;NAME&gt;
    /// it is, or null for any other host.
    /// </summary>
    public static string? OriginFor(string host) => OriginFor(host, Environment.GetEnvironmentVariables());

    public static string? OriginFor(string host, IDictionary variables)
    {
        var colon = host.LastIndexOf(':');
        var hostname = (colon >= 0 && !host.EndsWith(']') ? host[..colon] : host).TrimEnd('.');
        if (hostname.Length == 0)
            return null;
        foreach (DictionaryEntry variable in variables)
        {
            if (variable.Key is string key && key.StartsWith("HOST_", StringComparison.Ordinal)
                && string.Equals(variable.Value as string, hostname, StringComparison.OrdinalIgnoreCase)
                && variables["ORIGIN_" + key["HOST_".Length..]] is string { Length: > 0 } origin)
                return origin;
        }
        return null;
    }

    /// <summary>Name is a host's variable: HOST_ADMIN_THEPLOT for HOST_ and admin-theplot.</summary>
    public static string Name(string prefix, string host) =>
        prefix + host.ToUpperInvariant().Replace('-', '_');

    private static string? Variable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
