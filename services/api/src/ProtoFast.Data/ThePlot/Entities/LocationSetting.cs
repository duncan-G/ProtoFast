namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A <see cref="Location"/>'s slugline prefix: <c>INT.</c> or <c>EXT.</c>.
/// Stored by name, so members can be reordered or added without a data migration; renaming one does need one.
/// </summary>
public enum LocationSetting
{
    Interior,
    Exterior,
}
