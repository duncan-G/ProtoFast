using ProtoFast.Api.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

public sealed record ResearchProgress(ResearchAction Action, string Subject) : ResearchUpdate;
