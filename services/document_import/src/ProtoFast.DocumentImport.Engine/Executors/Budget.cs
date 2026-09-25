namespace ProtoFast.DocumentImport.Engine;

public sealed record Budget(decimal MaxCost, TimeSpan MaxDuration)
{
    public static readonly Budget Unbounded = new(decimal.MaxValue, Timeout.InfiniteTimeSpan);
}
