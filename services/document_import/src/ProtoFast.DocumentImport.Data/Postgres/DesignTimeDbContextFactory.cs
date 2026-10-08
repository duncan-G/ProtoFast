using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProtoFast.DocumentImport.Data.Postgres;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model offline; the connection string is a
/// placeholder. Run from <c>services/document_import/src/ProtoFast.DocumentImport.Data</c>:
/// <c>dotnet ef migrations add &lt;Name&gt;</c>.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WorkflowEngineDbContext>
{
    public WorkflowEngineDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__protofast")
            ?? "Host=localhost;Port=5432;Database=protofast;Username=protofast;Password=protofast";

        var options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseWorkflowEngineNpgsql(connectionString)
            .Options;

        return new WorkflowEngineDbContext(options);
    }
}
