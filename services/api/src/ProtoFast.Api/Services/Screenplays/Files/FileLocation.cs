using LocationSettingRecord = ProtoFast.Data.ThePlot.Entities.LocationSetting;

namespace ProtoFast.Api.Services.Screenplays.Files;

public sealed class FileLocation
{
    /// <summary>Unique among the file's locations; only rows in the same file refer to it.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required LocationSettingRecord Setting { get; init; }

    public required int Hue { get; init; }
}
