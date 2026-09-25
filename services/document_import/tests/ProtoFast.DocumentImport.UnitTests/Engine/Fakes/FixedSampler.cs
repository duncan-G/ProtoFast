using ProtoFast.DocumentImport.Engine.Scheduling;

namespace ProtoFast.DocumentImport.UnitTests.Engine.Fakes;

internal sealed class FixedSampler : IShadowSampler
{
    public double Rate { get; set; }
    public bool Take(double rate) => Rate >= 1;
}
