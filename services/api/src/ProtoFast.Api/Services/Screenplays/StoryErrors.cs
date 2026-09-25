using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ProtoFast.Api.Services.Screenplays;

/// <summary>The messages reach writers verbatim, so they are worded for them.</summary>
public static class StoryErrors
{
    public const string StoryNotFound = "That story could not be found.";
    public const string SceneNotFound = "That scene could not be found.";
    public const string ContainerNotFound = "That part of the story could not be found.";

    public static RpcException NotFound(string message) => new(new Status(StatusCode.NotFound, message));

    public static RpcException Invalid(string message) => new(new Status(StatusCode.InvalidArgument, message));

    public static RpcException AlreadyExists(string message) => new(new Status(StatusCode.AlreadyExists, message));

    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
