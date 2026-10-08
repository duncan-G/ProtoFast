namespace ProtoFast.DocumentImport.Engine.Storage;

/// <param name="RunId">The run working the source; null while preparing.</param>
/// <param name="StageId">The stage that run last started.</param>
/// <param name="Message">Written for the person: what went wrong, while retrying or once failed.</param>
/// <param name="ResultId">What the source became, once finished.</param>
/// <param name="Cost">
/// USD spent on the source so far, across every attempt at it. The ledger adds each stage recorded
/// under <paramref name="RunId"/>; only a source's first report sets it. Whole shadow runs are not counted.
/// </param>
public sealed record RunProgress(
    RunPhase Phase,
    string? RunId = null,
    string? StageId = null,
    string? Message = null,
    string? ResultId = null,
    decimal Cost = 0);
