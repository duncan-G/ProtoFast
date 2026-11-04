using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public sealed class UnitOfWorkFactory<TContext>(TContext dbContext) : IUnitOfWorkFactory<TContext>
    where TContext : IDbContext
{
    private readonly UnitOfWorkFactory _factory = new(dbContext);

    public IUnitOfWork CreateReadOnly(string operationName) => _factory.CreateReadOnly(operationName);

    public IUnitOfWork CreateReadWrite(string operationName) => _factory.CreateReadWrite(operationName);
}
