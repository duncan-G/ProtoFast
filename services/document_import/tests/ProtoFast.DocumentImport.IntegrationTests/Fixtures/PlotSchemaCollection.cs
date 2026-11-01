using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Fixtures;

/// <summary>Classes that migrate the plot schema in the shared database; two migrating at once collide.</summary>
[CollectionDefinition(Name)]
public sealed class PlotSchemaCollection
{
    public const string Name = "plot schema";
}
