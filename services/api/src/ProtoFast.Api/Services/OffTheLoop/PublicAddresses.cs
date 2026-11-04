using System.Net;
using System.Net.Sockets;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>
/// Connects only to addresses on the public internet, so a link can't reach this host, its network or
/// the cloud's metadata service. Checked as each connection opens, it holds for redirects and rebound DNS.
/// </summary>
public static class PublicAddresses
{
    private static readonly IPNetwork[] NotPublicV4 =
    [
        .. new[]
        {
            "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
            "192.0.0.0/24", "192.0.2.0/24", "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24",
            "203.0.113.0/24", "224.0.0.0/3",
        }.Select(network => IPNetwork.Parse(network)),
    ];

    private static readonly IPNetwork GlobalV6 = IPNetwork.Parse("2000::/3");

    private static readonly IPNetwork DocumentationV6 = IPNetwork.Parse("2001:db8::/32");

    private static readonly IPNetwork Nat64 = IPNetwork.Parse("64:ff9b::/96");

    public static SocketsHttpHandler Handler() => new()
    {
        ConnectCallback = ConnectAsync,
        // Through a proxy, the proxy's address would be the one checked.
        UseProxy = false,
        MaxAutomaticRedirections = 5,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
    };

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublic(address.MapToIPv4());
        }

        if (Nat64.Contains(address))
        {
            return IsPublic(new IPAddress(address.GetAddressBytes()[12..]));
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !NotPublicV4.Any(network => network.Contains(address)),
            AddressFamily.InterNetworkV6 => GlobalV6.Contains(address) && !DocumentationV6.Contains(address),
            _ => false,
        };
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = (await Dns.GetHostAddressesAsync(host, cancellationToken)).Where(IsPublic).ToArray();
        if (addresses.Length == 0)
        {
            throw new HttpRequestException($"{host} isn’t on the public internet.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
