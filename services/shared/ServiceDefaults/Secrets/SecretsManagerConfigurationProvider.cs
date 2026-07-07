using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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
        new(() => new AmazonSecretsManagerClient());

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
