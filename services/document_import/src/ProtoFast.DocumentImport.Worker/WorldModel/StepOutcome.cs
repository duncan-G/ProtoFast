namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>What a finished step records: the model call it made, if any, and what it changed.</summary>
public sealed record StepOutcome(
    string? ModelId,
    long InputTokens,
    long OutputTokens,
    decimal Cost,
    string? Prompt,
    string? Reply,
    string Summary);
