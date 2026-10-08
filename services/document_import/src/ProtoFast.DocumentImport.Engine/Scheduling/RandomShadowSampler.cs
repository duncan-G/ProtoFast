namespace ProtoFast.DocumentImport.Engine.Scheduling;

public sealed class RandomShadowSampler : IShadowSampler
{
    public bool Take(double rate) => rate > 0 && Random.Shared.NextDouble() < rate;
}
