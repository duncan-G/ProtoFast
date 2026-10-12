namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>An observation that disagreed with the standing fact without the standing to replace it.</summary>
public sealed record Conflict(ObservationRow Observation, Fact Standing);
