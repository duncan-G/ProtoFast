namespace ProtoFast.AppHost.LocalStack;

public class LocalStackResource(string name) : ContainerResource(name), IResourceWithServiceDiscovery
{
    internal const string GatewayEndpointName = "gateway";
    internal const string GatewayHost = "localhost.localstack.cloud";
    internal const int GatewayPort = 4566;

    internal static string GatewayHostAndPort => $"{GatewayHost}:{GatewayPort}";
    internal static string GatewayUrl => $"https://{GatewayHost}";

}
