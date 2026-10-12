namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>What applying a reading changed, and which of its edges could not be taken.</summary>
public sealed record AppliedScene(int PartsAdded, int ObjectsAdded, int AliasesAdded, int Observations, IReadOnlyList<string> Skipped)
{
    public string Summary
    {
        get
        {
            var line = $"{PartsAdded} new {(PartsAdded == 1 ? "part" : "parts")}, {ObjectsAdded} new {(ObjectsAdded == 1 ? "object" : "objects")}, "
                       + $"{AliasesAdded} {(AliasesAdded == 1 ? "alias" : "aliases")}, {Observations} {(Observations == 1 ? "observation" : "observations")}";
            return Skipped.Count == 0 ? line : $"{line}; skipped {Skipped.Count}: {string.Join("; ", Skipped)}";
        }
    }
}
