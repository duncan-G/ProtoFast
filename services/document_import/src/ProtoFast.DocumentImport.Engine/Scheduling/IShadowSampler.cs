namespace ProtoFast.DocumentImport.Engine.Scheduling;

public interface IShadowSampler
{
    bool Take(double rate);
}
