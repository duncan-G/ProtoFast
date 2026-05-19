using System.Diagnostics;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public sealed class UnitOfWorkFactory(IDbContext dbContext) : IUnitOfWorkFactory
{
    private static readonly ActivitySource ActivitySource = new("ProtoFast.UnitOfWork");

    public IUnitOfWork CreateReadOnly(string operationName)
    {
        Activity? activity = ActivitySource.StartActivity($"Start UnitOfWork: {operationName}");

        if (UnitOfWorkContext.Current != null)
        {
            return new NestedUnitOfWork(UnitOfWorkContext.Current, isReadWrite: false, activity);
        }

        return new ReadOnlyUnitOfWork(dbContext, activity);
    }

    public IUnitOfWork CreateReadWrite(string operationName)
    {
        Activity? activity = ActivitySource.StartActivity($"Start UnitOfWork: {operationName}");

        if (UnitOfWorkContext.Current != null)
        {
            VerifyParentSupportsReadWrite(UnitOfWorkContext.Current);
            return new NestedUnitOfWork(UnitOfWorkContext.Current, isReadWrite: true, activity);
        }

        return new ReadWriteUnitOfWork(dbContext, activity);
    }

    private static void VerifyParentSupportsReadWrite(IUnitOfWork parent)
    {
        IUnitOfWork root = GetRoot(parent);

        if (root is ReadOnlyUnitOfWork)
        {
            throw new InvalidOperationException(
                "Cannot create a read-write unit of work nested inside a read-only unit of work.");
        }
    }

    private static IUnitOfWork GetRoot(IUnitOfWork unitOfWork)
    {
        while (unitOfWork is NestedUnitOfWork nested)
        {
            unitOfWork = nested.Parent;
        }

        return unitOfWork;
    }
}
