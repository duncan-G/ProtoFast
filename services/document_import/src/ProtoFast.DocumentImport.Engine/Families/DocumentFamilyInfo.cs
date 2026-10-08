namespace ProtoFast.DocumentImport.Engine.Families;

/// <summary>
/// What is written about a family, by the classifier when it opens one or by an operator. The
/// description is what the classifier matches the next document against.
/// </summary>
public sealed record DocumentFamilyInfo(
    string Family,
    string DisplayName,
    string Description,
    string? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
