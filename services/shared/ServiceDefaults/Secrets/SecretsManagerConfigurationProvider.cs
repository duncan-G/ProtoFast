using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;

namespace ProtoFast.ServiceDefaults.Secrets;

internal sealed class SecretsManagerConfigurationProvider(SecretsManagerConfigurationSource source)
    : ConfigurationProvider, IDisposable
{
    private const string SharedPrefix = "Shared_";

    private readonly CancellationTokenSource _cancellation = new();

    private readonly Lazy<IAmazonSecretsManager> _client =
        new(() => CreateClient(source.SecretManagerOptions));

    private PeriodicTimer? _reloadTimer;

    public void Dispose()
    {
        _cancellation.Cancel();
        _reloadTimer?.Dispose();
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    public override void Load()
    {
        LoadAsync().GetAwaiter().GetResult();
    }

    private async Task LoadAsync()
    {
        var secretId = source.SecretManagerOptions.SecretId;
        var secretJson = (await _client.Value.GetSecretValueAsync(
                new GetSecretValueRequest { SecretId = secretId }, _cancellation.Token).ConfigureAwait(false))
            .SecretString ?? "{}";

        var entries = JsonSerializer.Deserialize<Dictionary<string, string?>>(secretJson) ??
                      new Dictionary<string, string?>();
        var scopedEntries = ApplyScoping(entries, source.SecretManagerOptions);

        Data = scopedEntries;

        OnReload();
        StartReloadTimerOnce();
    }

    /// <summary>
    /// Builds the client from explicit options first, then the SDK's own chain. The
    /// parameterless client resolves nothing when the active profile has no region
    /// (SSO profiles often don't) and fails with an opaque message, so the region is
    /// resolved here and the failure is reported with the fix.
    /// </summary>
    private static IAmazonSecretsManager CreateClient(SecretsManagerOptions options)
    {
        var awsOptions = new AWSOptions();

        if (!string.IsNullOrWhiteSpace(options.Profile))
        {
            awsOptions.Profile = options.Profile;
        }

        var region = options.Region;
        if (string.IsNullOrWhiteSpace(region))
        {
            region = Environment.GetEnvironmentVariable("AWS_REGION")
                     ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            awsOptions.Region = RegionEndpoint.GetBySystemName(region);
        }

        try
        {
            return awsOptions.CreateServiceClient<IAmazonSecretsManager>();
        }
        catch (AmazonClientException ex)
        {
            throw new InvalidOperationException(
                $"""
                 Could not create a Secrets Manager client for secret '{options.SecretId}': {ex.Message}
                 Set one of:
                   - AWS_REGION in the environment (the AppHost injects it in dev; deploy/ sets it in prod)
                   - Secrets:Region in configuration
                   - a region on the active profile: aws configure set region <region> --profile <profile>
                 """,
                ex);
        }
    }

    private static FrozenDictionary<string, string?> ApplyScoping(
        IReadOnlyDictionary<string, string?> entries, SecretsManagerOptions options)
    {
        var prefixes = new[] { SharedPrefix, options.Prefix };
        var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawKey, secret) in entries)
        {
            if (string.IsNullOrEmpty(secret))
            {
                continue;
            }

            if (TryScopeKey(rawKey, prefixes, out var key))
            {
                resolved[key] = secret;
            }
        }

        return resolved.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryScopeKey(
        string rawKey, string?[] prefixes, [NotNullWhen(true)] out string? key)
    {
        foreach (var prefix in prefixes)
        {
            // Replaces prefix__key__name with key:name
            if (prefix is null || !rawKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            key = rawKey[prefix.Length..].Replace("__", ":", StringComparison.Ordinal);
            return true;
        }

        key = null;
        return false;
    }

    private void StartReloadTimerOnce()
    {
        if (_reloadTimer is not null || source.SecretManagerOptions.ReloadAfter is not { } interval)
        {
            return;
        }

        _reloadTimer = new PeriodicTimer(interval);
        _ = Task.Run(async () =>
        {
            while (await _reloadTimer.WaitForNextTickAsync(_cancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    await LoadAsync().ConfigureAwait(false);
                }
                catch when (_cancellation.IsCancellationRequested)
                {
                }
                catch
                {
                    /* swallow or log; loop continues */
                }
            }
        });
    }
}
