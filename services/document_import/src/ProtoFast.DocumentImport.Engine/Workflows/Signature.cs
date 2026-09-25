namespace ProtoFast.DocumentImport.Engine.Workflows;

/// <param name="Family">
/// Documents similar enough to share one workflow and policy, e.g. one vendor's invoices.
/// </param>
public sealed record Signature(string Family, IReadOnlyDictionary<string, string> Facets);
