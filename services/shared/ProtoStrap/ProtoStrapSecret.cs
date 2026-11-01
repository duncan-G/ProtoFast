using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;

namespace ProtoFast.ProtoStrap;

/// <summary>
/// ProtoStrapSecret reads a component's keys from the environment's secret, SECRET_ID
/// (ProtoStrap's docs/secrets.md): the Shared_ keys, then its own under SECRET_PREFIX, which
/// win. Under <c>aspire run</c> SECRET_ID is dev's secret, protofast/dev, read the same way.
/// </summary>
public static class ProtoStrapSecret
{
    public const string SharedPrefix = "Shared_";

    /// <summary>
    /// AddProtoStrapSecret adds the component's keys, read once at startup: ProtoStrap replaces
    /// the replicas when they change. Added last, they win over appsettings and the environment.
    /// </summary>
    public static IConfigurationBuilder AddProtoStrapSecret(this IConfigurationBuilder configuration) =>
        configuration.AddInMemoryCollection(Keys(
            Environment.GetEnvironmentVariable("SECRET_ID"),
            Environment.GetEnvironmentVariable("SECRET_PREFIX"),
            Read));

    /// <summary>
    /// Keys returns the component's keys in configuration form, InternalJwt:KeyId for
    /// InternalJwt__KeyId, or none without a secret. Keys of other prefixes are other components'.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>> Keys(string? secretId, string? prefix, Func<string, string> read)
    {
        if (string.IsNullOrEmpty(secretId))
            return [];
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(read(secretId))
            ?? throw new InvalidOperationException($"The secret {secretId} isn't a JSON object of strings");

        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (key.StartsWith(SharedPrefix, StringComparison.Ordinal))
                keys[key[SharedPrefix.Length..]] = value;
        }
        if (!string.IsNullOrEmpty(prefix))
        {
            foreach (var (key, value) in values)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    keys[key[prefix.Length..]] = value; // Its own keys win.
            }
        }
        return keys
            .Where(pair => pair.Value.Length > 0)
            .Select(pair => KeyValuePair.Create(pair.Key.Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal), (string?)pair.Value))
            .ToList();
    }

    // Read uses the SDK's default credentials and AWS_REGION: the role ProtoStrap's agent serves, or
    // under aspire run the developer's AWS_PROFILE.
    private static string Read(string secretId)
    {
        using var client = new AmazonSecretsManagerClient();
        return client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretId }).GetAwaiter().GetResult().SecretString;
    }
}
