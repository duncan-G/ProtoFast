using Amazon.Extensions.NETCore.Setup;
using Microsoft.Extensions.Configuration;

namespace ProtoFast.ServiceDefaults.Secrets;

internal sealed class SecretsManagerConfigurationSource(
    SecretsManagerOptions secretManagerOptions)
    : IConfigurationSource
{
    public SecretsManagerOptions SecretManagerOptions => secretManagerOptions;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SecretsManagerConfigurationProvider(this);
    }
}
