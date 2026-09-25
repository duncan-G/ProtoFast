namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A scene heading's time of day (<c>— NIGHT</c>).
/// Stored by name, so members can be reordered or added without a data migration; renaming one does need one.
/// </summary>
public enum TimeOfDay
{
    Day,
    Night,
    Dawn,
    Dusk,
    Continuous,
    Later,
}
