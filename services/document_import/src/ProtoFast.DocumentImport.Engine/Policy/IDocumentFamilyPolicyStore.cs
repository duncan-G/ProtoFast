namespace ProtoFast.DocumentImport.Engine.Policy;

public interface IDocumentFamilyPolicyStore
{
    Task<DocumentFamilyPolicy> GetAsync(string family, CancellationToken ct);
    Task PutAsync(DocumentFamilyPolicy policy, CancellationToken ct);
}
