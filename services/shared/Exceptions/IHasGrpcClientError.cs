namespace ProtoFast.Exceptions;

public interface IHasGrpcClientError
{
    GrpcErrorDescriptor ToGrpcError();
}
