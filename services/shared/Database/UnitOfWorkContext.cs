using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public static class UnitOfWorkContext
{
    private static readonly AsyncLocal<IUnitOfWork?> _current = new();

    public static IUnitOfWork? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    public static bool HasCurrent => _current.Value != null;
}
