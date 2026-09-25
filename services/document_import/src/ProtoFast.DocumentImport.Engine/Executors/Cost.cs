namespace ProtoFast.DocumentImport.Engine.Executors;

public sealed record Cost(decimal Amount, TimeSpan Duration)
{
    public static readonly Cost Zero = new(0, TimeSpan.Zero);
}
