namespace ProtoFast.DocumentImport.Engine.Policy;

public sealed record Thresholds(
    double PromoteAt = 0.95,
    double DemoteAt = 0.80,
    int MinObservations = 20,
    double HalfLifeDays = 30,
    double ShadowSampleRate = 0.2,
    int MineAfterRuns = 10,
    double MinSupport = 0.8,
    int MinDemoteObservations = 5)
{
    public bool Ready(Confidence c) => c.Mean >= PromoteAt && c.Observations >= MinObservations;

    // The prior's mean (0.5) is below DemoteAt, so without an evidence floor a tier just demoted
    // to would be demoted again by its first outcome.
    public bool ShouldDemote(Confidence c) => c.Mean < DemoteAt && c.Observations >= MinDemoteObservations;
}
