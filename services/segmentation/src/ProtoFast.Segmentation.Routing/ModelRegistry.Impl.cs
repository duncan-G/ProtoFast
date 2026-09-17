using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Data;

namespace ProtoFast.Segmentation.Routing;

/// <summary>
/// Configuration for the catalogue, Postgres for the qualifications (plan §14.2).
///
/// <para>The split matters: the catalogue ships with the image and rolls back with it, while
/// qualification is evidence gathered by running the gold set, which every worker must see the
/// same answer for the moment it lands. Caching the lookup for a minute keeps a fan-out of two
/// thousand windows from turning into two thousand identical queries, and a minute of staleness on
/// "is this model qualified?" changes nothing that a deploy would not change anyway.</para>
/// </summary>
public sealed class ModelRegistry(
    IOptions<RoutingOptions> options,
    IServiceScopeFactory scopes,
    IMemoryCache cache) : IModelRegistry
{
    private static readonly TimeSpan QualificationCacheTtl = TimeSpan.FromMinutes(1);

    public IReadOnlyList<ModelDescriptor> Models => options.Value.Models;

    public IReadOnlyList<PoolDescriptor> Pools => options.Value.Pools;

    public ModelDescriptor? Find(string key) =>
        Models.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

    public PoolDescriptor PoolFor(ModelDescriptor model) =>
        Pools.FirstOrDefault(p => p.Key == model.LimitPool)
        ?? new PoolDescriptor { Key = model.LimitPool, Adaptive = true };

    public async Task<bool> IsQualifiedAsync(
        ModelDescriptor model, AgentRole role, string promptVersion, CancellationToken ct = default)
    {
        // A presumed-qualified role is the bootstrap escape hatch (see ModelDescriptor): on a
        // fresh deployment nothing has been evaluated yet, and a registry that answers "no" to
        // every question leaves the router with no model for any role.
        if (model.PresumedQualifiedRoles.Contains(role))
        {
            return true;
        }

        var cacheKey = $"seg:qualified:{model.Key}:{role}:{promptVersion}";
        if (cache.TryGetValue<bool>(cacheKey, out var cached))
        {
            return cached;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var qualified = await db.Qualifications
            .AsNoTracking()
            .AnyAsync(
                q => q.ModelKey == model.Key
                    && q.Role == role
                    && q.PromptVersion == promptVersion
                    && q.Qualified,
                ct);

        cache.Set(cacheKey, qualified, QualificationCacheTtl);
        return qualified;
    }
}
