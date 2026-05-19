namespace ProtoFast.AppHost.OpenTelemetryCollector;

public class OpenTelemetryCollectorResource(string name) : ContainerResource(name), IResourceWithServiceDiscovery
{
    internal const string OtlpGrpcEndpointName = "otlp-grpc";
    internal const string OtlpHttpEndpointName = "otlp-http";
}
