using Grpc.Core;

namespace ProtoFast.Exceptions;

public sealed record GrpcErrorDescriptor(
    StatusCode StatusCode,
    string ErrorCode,
    string? Message = null)
{
    public RpcException ToRpcException()
    {
        var metadata = new Metadata { { "Error-Code", ErrorCode } };

        var status = new Status(StatusCode, Message ?? string.Empty);
        return new RpcException(status, metadata, Message ?? string.Empty);
    }
}
