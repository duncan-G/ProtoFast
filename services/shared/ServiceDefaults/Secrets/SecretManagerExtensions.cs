using Microsoft.Extensions.Configuration;

namespace ProtoFast.ServiceDefaults.Secrets;

public static class SecretsManagerExtensions
{
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        Action<SecretsManagerOptions> configureOptions)
    {
        var options = new SecretsManagerOptions();
        configureOptions(options);
        if (string.IsNullOrEmpty(options.SecretId))
        {
            throw new InvalidOperationException("SecretId cannot be null or empty.");
        }

        var source = new SecretsManagerConfigurationSource(options);
        builder.Add(source);
        return builder;
    }
}
