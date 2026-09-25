namespace ProtoFast.DocumentImport.Engine.Policy;

/// <summary>Beta posterior on pass rate.</summary>
public readonly record struct Confidence(double Alpha, double Beta)
{
    public static readonly Confidence Prior = new(1, 1);
    public double Mean         => Alpha / (Alpha + Beta);
    public double Observations => Alpha + Beta - 2;
    public Confidence Decay(double factor) => new(1 + (Alpha - 1) * factor, 1 + (Beta - 1) * factor);
    public Confidence Observe(bool pass, double weight) =>
        pass ? this with { Alpha = Alpha + weight } : this with { Beta = Beta + weight };
}
