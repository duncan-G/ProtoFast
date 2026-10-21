using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

public sealed record LocationInfo(Guid Id, string Name, Guid? InsideLocationId, LocationSetting? Setting = null);
