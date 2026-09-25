using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProtoFast.DocumentImport.Data.Entities;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Storage;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.Data;

/// <summary>
/// Content is frozen in the object store under its hash, with the version and promotion stored
/// separately so promoting never rewrites content. Content is hashed without its version or
/// promotion, so republishing an unchanged spec reuses the stored object.
/// </summary>
public sealed class PostgresRegistry(
    IDbContextFactory<WorkflowEngineDbContext> contexts,
    IObjectStore objects,
    TimeProvider time) : IRegistry
{
    private const int PublishAttempts = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // Computed properties such as WorkflowDefinition.TerminalStages are not content.
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ConcurrentDictionary<string, byte[]> _content = new(StringComparer.Ordinal);

    public async Task<Playbook> ResolveAsync(PlaybookRef reference, CancellationToken ct)
    {
        var entry = await FindAsync(RegistryEntryKind.Playbook, reference.Id, reference.Version, ct);
        var playbook = await ReadAsync<Playbook>(RegistryKeys.Playbook(entry.ContentHash), ct);
        return playbook with { Ref = reference };
    }

    public async Task<ExecutorSpec> ResolveAsync(ExecutorRef reference, CancellationToken ct)
    {
        var entry = await FindAsync(RegistryEntryKind.Executor, reference.Id, reference.Version, ct);
        var spec = await ReadAsync<ExecutorSpec>(RegistryKeys.Executor(entry.ContentHash), ct);
        return spec with { Ref = reference, Promoted = entry.Promoted };
    }

    public async Task<WorkflowDefinition> ResolveAsync(WorkflowRef reference, CancellationToken ct)
    {
        var entry = await FindAsync(RegistryEntryKind.Workflow, reference.Id, reference.Version, ct);
        var workflow = await ReadAsync<WorkflowDefinition>(RegistryKeys.Workflow(entry.ContentHash), ct);
        return workflow with { Ref = reference };
    }

    public async Task<PlaybookRef> PublishAsync(Playbook playbook, CancellationToken ct)
    {
        var id = playbook.Ref.Id;
        var version = await PublishAsync(
            RegistryEntryKind.Playbook, id, playbook with { Ref = new PlaybookRef(id, 0) }, RegistryKeys.Playbook, promoted: false, ct);
        return new PlaybookRef(id, version);
    }

    public async Task<ExecutorRef> PublishAsync(ExecutorSpec spec, CancellationToken ct)
    {
        var id = spec.Ref.Id;
        var version = await PublishAsync(
            RegistryEntryKind.Executor, id, spec with { Ref = new ExecutorRef(id, 0), Promoted = false },
            RegistryKeys.Executor, spec.PromotedOnPublish, ct);
        return new ExecutorRef(id, version);
    }

    public async Task<WorkflowRef> PublishAsync(WorkflowDefinition workflow, CancellationToken ct)
    {
        var id = workflow.Ref.Id;
        var version = await PublishAsync(
            RegistryEntryKind.Workflow, id, workflow with { Ref = new WorkflowRef(id, 0) }, RegistryKeys.Workflow, promoted: false, ct);
        return new WorkflowRef(id, version);
    }

    public async Task<string> PublishCodeAsync(Stream code, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await code.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var hash = Hash(bytes);
        await WriteOnceAsync(RegistryKeys.Code(hash), bytes, "application/octet-stream", hash, ct);
        return hash;
    }

    public async Task<Stream> OpenCodeAsync(string hash, CancellationToken ct) =>
        (IsHash(hash) ? await objects.OpenReadAsync(RegistryKeys.Code(hash), ct) : null)
        ?? throw new KeyNotFoundException($"No code with hash {hash}.");

    public async Task<bool> CodeExistsAsync(string hash, CancellationToken ct) =>
        IsHash(hash) && await objects.ExistsAsync(RegistryKeys.Code(hash), ct);

    public Task PromoteAsync(ExecutorRef reference, CancellationToken ct) =>
        PromoteAsync(RegistryEntryKind.Executor, reference.Id, reference.Version, ct);

    public Task PromoteAsync(WorkflowRef reference, CancellationToken ct) =>
        PromoteAsync(RegistryEntryKind.Workflow, reference.Id, reference.Version, ct);

    public async Task<bool> IsPromotedAsync(WorkflowRef reference, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.RegistryEntries.AnyAsync(
            e => e.Kind == RegistryEntryKind.Workflow && e.Id == reference.Id && e.Version == reference.Version && e.Promoted,
            ct);
    }

    private async Task<int> PublishAsync<T>(
        RegistryEntryKind kind, string id, T content, Func<string, string> keyFor, bool promoted, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(content, Json);
        var hash = Hash(bytes);
        await WriteOnceAsync(keyFor(hash), bytes, "application/json", hash, ct);

        for (var attempt = 1; ; attempt++)
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var latest = await db.RegistryEntries
                .Where(e => e.Kind == kind && e.Id == id)
                .MaxAsync(e => (int?)e.Version, ct) ?? 0;

            db.RegistryEntries.Add(new RegistryEntry
            {
                Kind = kind,
                Id = id,
                Version = latest + 1,
                ContentHash = hash,
                Promoted = promoted,
                PublishedAt = time.GetUtcNow(),
            });

            try
            {
                await db.SaveChangesAsync(ct);
                return latest + 1;
            }
            catch (DbUpdateException e) when (IsUniqueViolation(e) && attempt < PublishAttempts)
            {
                // Another publisher took this version first.
            }
        }
    }

    private async Task PromoteAsync(RegistryEntryKind kind, string id, int version, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var updated = await db.RegistryEntries
            .Where(e => e.Kind == kind && e.Id == id && e.Version == version)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.Promoted, true), ct);
        if (updated == 0)
        {
            throw new KeyNotFoundException($"{kind} {id}@{version} does not exist.");
        }
    }

    private async Task<RegistryEntry> FindAsync(RegistryEntryKind kind, string id, int version, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.RegistryEntries.AsNoTracking()
                   .SingleOrDefaultAsync(e => e.Kind == kind && e.Id == id && e.Version == version, ct)
               ?? throw new KeyNotFoundException($"{kind} {id}@{version} does not exist.");
    }

    private async Task<T> ReadAsync<T>(string key, CancellationToken ct)
    {
        if (!_content.TryGetValue(key, out var bytes))
        {
            await using var stream = await objects.OpenReadAsync(key, ct)
                ?? throw new InvalidOperationException($"Registry content {key} is missing from the object store.");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            bytes = _content.GetOrAdd(key, buffer.ToArray());
        }

        return JsonSerializer.Deserialize<T>(bytes, Json)!;
    }

    // Frozen objects cannot be overwritten, and identical content is already there.
    private async Task WriteOnceAsync(string key, byte[] bytes, string contentType, string hash, CancellationToken ct)
    {
        if (!await objects.ExistsAsync(key, ct))
        {
            await objects.WriteFrozenBytesAsync(key, bytes, contentType, hash, ct);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // A hash is used to build an object key, so it must not be able to carry a path.
    private static bool IsHash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);

    private static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
