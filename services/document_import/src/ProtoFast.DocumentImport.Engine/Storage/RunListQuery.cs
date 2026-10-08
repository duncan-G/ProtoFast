using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Engine.Storage;

/// <param name="Family">A bare family name matches every generation of it; null matches all.</param>
public sealed record RunListQuery(
    string? Family = null,
    RunMode? Mode = null,
    RunStatus? Status = null,
    int Page = 0,
    int PageSize = 25);
