using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Entities;

namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>A run as a watch reads it; <paramref name="Changed"/> is false when only its status fields were read.</summary>
public sealed record WatchedRun(ModelRun Run, bool Changed, RunProgress Progress, RunResult? Result);
