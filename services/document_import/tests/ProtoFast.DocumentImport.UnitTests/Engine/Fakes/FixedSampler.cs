using ProtoFast.DocumentImport.Engine;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

internal sealed class FixedSampler : IShadowSampler
{
    public double Rate { get; set; }
    public bool Take(double rate) => Rate >= 1;
}
