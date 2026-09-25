namespace ProtoFast.DocumentImport.Engine;

public sealed class RandomShadowSampler : IShadowSampler
{
    public bool Take(double rate) => rate > 0 && Random.Shared.NextDouble() < rate;
}
