namespace ProtoFast.Database.Abstractions;

public interface IUnitOfWorkFactory
{
    IUnitOfWork CreateReadOnly(string operationName);

    IUnitOfWork CreateReadWrite(string operationName);
}
