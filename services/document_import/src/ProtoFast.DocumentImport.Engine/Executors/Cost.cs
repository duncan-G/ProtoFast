namespace ProtoFast.DocumentImport.Engine;

public sealed record Cost(decimal Amount, TimeSpan Duration)
{
    public static readonly Cost Zero = new(0, TimeSpan.Zero);
}
