using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Engine.Families;

public sealed record FamilyExecutor(ExecutorRef Ref, DateTimeOffset AddedAt);
