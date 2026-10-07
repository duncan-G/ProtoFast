using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Families;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryDocumentFamilyRegistry : IDocumentFamilyRegistry
{
    private readonly ConcurrentDictionary<string, DocumentFamilyInfo> _infos = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<DocumentFamilyInfo>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DocumentFamilyInfo>>(
            _infos.Values.OrderBy(i => i.Family, StringComparer.Ordinal).ToList());

    public Task CreateAsync(DocumentFamilyInfo info, CancellationToken ct)
    {
        if (!_infos.TryAdd(info.Family, info))
        {
            throw new InvalidOperationException($"Document family '{info.Family}' is already registered.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(DocumentFamilyInfo info, CancellationToken ct)
    {
        _infos.AddOrUpdate(
            info.Family,
            info,
            (_, existing) => existing with
            {
                DisplayName = info.DisplayName, Description = info.Description, UpdatedAt = info.UpdatedAt,
            });
        return Task.CompletedTask;
    }
}
